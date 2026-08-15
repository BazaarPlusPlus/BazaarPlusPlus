#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.ModApi.Http;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby;

internal sealed class MainMenuVersionCheckController : MonoBehaviour
{
    private static readonly Uri LatestManifestEndpoint = new(
        "https://bppinstaller.bazaarplusplus.com/latest.json"
    );
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly ReleaseManifestCheckLifecycle _lifecycle = new();
    private Task? _checkTask;
    private int _observedRevision;

    private void Awake() { }

    public void Initialize()
    {
        var httpClient = BppHttpClientFactory.Create(
            productVersion: BppPluginVersion.Current,
            userAgentSuffix: "VersionCheck",
            timeout: RequestTimeout
        );
        var lease = _lifecycle.Begin(httpClient);
        MainMenuVersionUpdateState.Reset();
        _observedRevision = MainMenuVersionUpdateState.Current.Revision;
        var client = new ReleaseManifestClient(httpClient, LatestManifestEndpoint);
        _checkTask = _lifecycle.RunAsync(lease, client.FetchAsync, ApplyLatestManifestResult);
    }

    private void Update()
    {
        RefreshLabelIfStateChanged();
        ObserveCompletedTask();
    }

    private void OnDestroy()
    {
        _lifecycle.Dispose();
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
        catch (Exception) { }
        finally
        {
            _checkTask = null;
        }
    }

    private void ApplyLatestManifestResult(ReleaseManifestFetchResult result)
    {
        if (!result.Succeeded)
        {
            if (result.FailureKind != ReleaseManifestFailureKind.Cancelled)
                ReportDegraded(result);
            return;
        }

        var latestVersion = result.Version!;
        var updateAvailable = MainMenuVersionComparer.IsUpdateAvailable(
            BppPluginVersion.Current,
            latestVersion
        );
        MainMenuVersionUpdateState.SetUpdateAvailable(updateAvailable);
        BppLog.DebugEvent(
            LobbyLogEvents.VersionCheckCompleted,
            () =>
                [
                    LobbyLogEvents.VersionCheckCompletedCurrentVersion.Bind(
                        BppPluginVersion.Current
                    ),
                    LobbyLogEvents.VersionCheckCompletedLatestVersion.Bind(latestVersion),
                    LobbyLogEvents.VersionCheckCompletedUpdateAvailable.Bind(updateAvailable),
                ]
        );
    }

    private static void ReportDegraded(ReleaseManifestFetchResult result)
    {
        var reasonCode = result.FailureKind switch
        {
            ReleaseManifestFailureKind.HttpFailureStatus => LobbyLogReasonCode.HttpFailureStatus,
            ReleaseManifestFailureKind.ManifestVersionMissing =>
                LobbyLogReasonCode.ManifestVersionMissing,
            ReleaseManifestFailureKind.RequestTimedOut => LobbyLogReasonCode.RequestTimedOut,
            _ => LobbyLogReasonCode.RequestException,
        };
        var fields = new[]
        {
            LobbyLogEvents.VersionCheckDegradedReasonCode.Bind(reasonCode),
            LobbyLogEvents.VersionCheckDegradedHttpStatus.Bind(result.HttpStatus),
            LobbyLogEvents.VersionCheckDegradedTimeoutMs.Bind(
                (int)RequestTimeout.TotalMilliseconds
            ),
        };
        if (result.Exception == null)
            BppLog.WarnEvent(LobbyLogEvents.VersionCheckDegraded, fields);
        else
            BppLog.WarnEvent(LobbyLogEvents.VersionCheckDegraded, result.Exception, fields);
    }
}
