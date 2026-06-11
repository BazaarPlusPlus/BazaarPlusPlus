#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Http;
using Newtonsoft.Json;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby;

internal sealed class MainMenuVersionCheckController : MonoBehaviour
{
    private const string Component = "MainMenuVersionCheck";
    private const string LatestManifestUrl = "https://bppinstaller.bazaarplusplus.com/latest.json";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private CancellationTokenSource? _shutdown;
    private HttpClient? _httpClient;
    private Task? _checkTask;
    private int _observedRevision;

    private void Awake() { }

    public void Initialize()
    {
        MainMenuVersionUpdateState.Reset();
        _observedRevision = MainMenuVersionUpdateState.Current.Revision;
        _shutdown = new CancellationTokenSource();
        _httpClient = BppHttpClientFactory.Create(
            productVersion: BppPluginVersion.Current,
            userAgentSuffix: "VersionCheck",
            timeout: RequestTimeout
        );
        _checkTask = CheckLatestVersionAsync(_shutdown.Token);
    }

    private void Update()
    {
        RefreshLabelIfStateChanged();
        ObserveCompletedTask();
    }

    private void OnDestroy()
    {
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
        catch (Exception ex)
        {
            BppLog.Warn(Component, $"Latest version check failed: {ex.Message}");
        }
        finally
        {
            _checkTask = null;
        }
    }

    private async Task CheckLatestVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = _httpClient;
            if (httpClient == null)
                return;

            using var response = await httpClient
                .GetAsync(LatestManifestUrl, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                BppLog.Warn(
                    Component,
                    $"Latest version check returned HTTP {(int)response.StatusCode}."
                );
                return;
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var parsed = JsonConvert.DeserializeObject<LatestManifest>(body);
            var latestVersion = parsed?.Version;
            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                BppLog.Warn(Component, "Latest version manifest did not include a version.");
                return;
            }

            var updateAvailable = MainMenuVersionComparer.IsUpdateAvailable(
                BppPluginVersion.Current,
                latestVersion
            );
            MainMenuVersionUpdateState.SetUpdateAvailable(updateAvailable);
            BppLog.Info(
                Component,
                $"Latest version check completed. current={BppPluginVersion.Current} latest={latestVersion.Trim()} update_available={updateAvailable}."
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (TaskCanceledException)
        {
            BppLog.Warn(
                Component,
                $"Latest version check timed out after {RequestTimeout.TotalSeconds:0}s."
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(Component, $"Latest version check failed: {ex.Message}");
        }
    }

    private sealed class LatestManifest
    {
        [JsonProperty("version")]
        public string? Version { get; set; }
    }
}
