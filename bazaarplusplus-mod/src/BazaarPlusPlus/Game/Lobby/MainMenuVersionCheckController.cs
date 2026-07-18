#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Http;
using Newtonsoft.Json;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby;

internal sealed class MainMenuVersionCheckController : MonoBehaviour
{
    private const string LatestManifestUrl = "https://bppinstaller.bazaarplusplus.com/latest.json";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private CancellationTokenSource? _shutdown;
    private HttpClient? _httpClient;
    private Task? _checkTask;
    private int _observedRevision;
    private int _generation;

    private void Awake() { }

    public void Initialize()
    {
        _shutdown?.Cancel();
        _shutdown?.Dispose();
        var generation = Interlocked.Increment(ref _generation);
        MainMenuVersionUpdateState.Reset();
        _observedRevision = MainMenuVersionUpdateState.Current.Revision;
        _shutdown = new CancellationTokenSource();
        _httpClient = BppHttpClientFactory.Create(
            productVersion: BppPluginVersion.Current,
            userAgentSuffix: "VersionCheck",
            timeout: RequestTimeout
        );
        _checkTask = CheckLatestVersionAsync(generation, _shutdown.Token);
    }

    private void Update()
    {
        RefreshLabelIfStateChanged();
        ObserveCompletedTask();
    }

    private void OnDestroy()
    {
        Interlocked.Increment(ref _generation);
        if (_shutdown != null)
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
            _shutdown = null;
        }

        _httpClient?.Dispose();
        _httpClient = null;
    }

    private void RefreshLabelIfStateChanged()
    {
        var snapshot = MainMenuVersionUpdateState.Current;
        if (snapshot.Revision == _observedRevision)
            return;

        _observedRevision = snapshot.Revision;
        MainMenuVersionLabelUpdater.RefreshCurrent();
    }

    private void ObserveCompletedTask()
    {
        if (_checkTask == null || !_checkTask.IsCompleted)
            return;

        try
        {
            _checkTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            _checkTask = null;
        }
    }

    private async Task CheckLatestVersionAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = _httpClient;
            if (httpClient == null)
                return;

            using var response = await httpClient
                .GetAsync(LatestManifestUrl, cancellationToken)
                .ConfigureAwait(false);
            if (!IsCurrent(generation, cancellationToken))
                return;
            if (!response.IsSuccessStatusCode)
            {
                ReportDegraded(
                    generation,
                    cancellationToken,
                    LobbyLogReasonCode.HttpFailureStatus,
                    (int)response.StatusCode
                );
                return;
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!IsCurrent(generation, cancellationToken))
                return;
            var parsed = JsonConvert.DeserializeObject<LatestManifest>(body);
            var latestVersion = parsed?.Version;
            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                ReportDegraded(
                    generation,
                    cancellationToken,
                    LobbyLogReasonCode.ManifestVersionMissing
                );
                return;
            }

            var updateAvailable = MainMenuVersionComparer.IsUpdateAvailable(
                BppPluginVersion.Current,
                latestVersion
            );
            if (!IsCurrent(generation, cancellationToken))
                return;
            MainMenuVersionUpdateState.SetUpdateAvailable(updateAvailable);
            BppLog.DebugEvent(
                LobbyLogEvents.VersionCheckCompleted,
                () =>
                    [
                        LobbyLogEvents.VersionCheckCompletedCurrentVersion.Bind(
                            BppPluginVersion.Current
                        ),
                        LobbyLogEvents.VersionCheckCompletedLatestVersion.Bind(
                            latestVersion.Trim()
                        ),
                        LobbyLogEvents.VersionCheckCompletedUpdateAvailable.Bind(updateAvailable),
                    ]
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (TaskCanceledException ex)
        {
            ReportDegraded(
                generation,
                cancellationToken,
                LobbyLogReasonCode.RequestTimedOut,
                exception: ex
            );
        }
        catch (Exception ex)
        {
            ReportDegraded(
                generation,
                cancellationToken,
                LobbyLogReasonCode.RequestException,
                exception: ex
            );
        }
    }

    private bool IsCurrent(int generation, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && Volatile.Read(ref _generation) == generation;

    private void ReportDegraded(
        int generation,
        CancellationToken cancellationToken,
        LobbyLogReasonCode reasonCode,
        int? httpStatus = null,
        Exception? exception = null
    )
    {
        if (!IsCurrent(generation, cancellationToken))
            return;
        var fields = new[]
        {
            LobbyLogEvents.VersionCheckDegradedReasonCode.Bind(reasonCode),
            LobbyLogEvents.VersionCheckDegradedHttpStatus.Bind(httpStatus),
            LobbyLogEvents.VersionCheckDegradedTimeoutMs.Bind(
                (int)RequestTimeout.TotalMilliseconds
            ),
        };
        if (exception == null)
            BppLog.WarnEvent(LobbyLogEvents.VersionCheckDegraded, fields);
        else
            BppLog.WarnEvent(LobbyLogEvents.VersionCheckDegraded, exception, fields);
    }

    private sealed class LatestManifest
    {
        [JsonProperty("version")]
        public string? Version { get; set; }
    }
}
