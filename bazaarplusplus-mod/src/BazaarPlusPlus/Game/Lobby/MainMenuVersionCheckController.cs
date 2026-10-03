#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.ModApi.Http;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby;

internal sealed class MainMenuVersionCheckController : MonoBehaviour
{
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
        var client = new ReleaseManifestClient(httpClient, ResolveManifestEndpoint());
        _checkTask = _lifecycle.RunAsync(lease, client.FetchAsync, ApplyLatestManifestResult);
    }

    // A release promoted for one platform only must be seen only by that platform.
    private static Uri ResolveManifestEndpoint() =>
        ReleaseManifestEndpoints.ForPlatformKey(
            Application.platform switch
            {
                RuntimePlatform.WindowsPlayer => ReleaseManifestEndpoints.WindowsPlatformKey,
                RuntimePlatform.OSXPlayer => ReleaseManifestEndpoints.MacPlatformKey,
                _ => null,
            }
        );

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
            new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.version_check.completed"),
            () =>
                [
                    ("current_version", BppPluginVersion.Current),
                    ("latest_version", latestVersion),
                    ("update_available", updateAvailable),
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
        var fields = new BppLogField[]
        {
            ("reason_code", reasonCode),
            ("http_status", result.HttpStatus),
            ("timeout_ms", (int)RequestTimeout.TotalMilliseconds),
        };
        if (result.Exception == null)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Lobby,
                    "lobby.version_check.degraded",
                    storm: ["reason_code"]
                ),
                fields
            );
        else
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Lobby,
                    "lobby.version_check.degraded",
                    storm: ["reason_code"]
                ),
                result.Exception,
                fields
            );
    }
}
