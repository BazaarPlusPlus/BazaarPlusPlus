#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.SelfUpdate;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby;

internal sealed class MainMenuVersionCheckController : MonoBehaviour
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    private readonly ReleaseManifestCheckLifecycle _lifecycle = new();
    private MainMenuSelfUpdate? _selfUpdate;
    private Task? _checkTask;
    private int _observedRevision;

    private void Awake() { }

    public void Initialize(IBppServices services)
    {
        var platformKey = DesktopPlatformKey.Current;
        _selfUpdate = new MainMenuSelfUpdate(
            installRoot: BepInEx.Paths.GameRootPath,
            dataRoot: services.Paths.RequireDataRoot(),
            platformKey,
            SelfUpdateHost.Desktop,
            RunLogSchema.LocalDatabaseSchemaVersion,
            () =>
                BppHttpClientFactory.Create(
                    productVersion: BppPluginVersion.Current,
                    platformKey: platformKey,
                    userAgentSuffix: "SelfUpdate",
                    timeout: DownloadTimeout
                )
        );
        var httpClient = BppHttpClientFactory.Create(
            productVersion: BppPluginVersion.Current,
            platformKey: platformKey,
            userAgentSuffix: "VersionCheck",
            timeout: RequestTimeout
        );
        var lease = _lifecycle.Begin(httpClient);
        _observedRevision = MainMenuVersionUpdateState.Current.Revision;
        // A release promoted for one platform only must be seen only by that platform.
        var client = new ReleaseManifestClient(
            httpClient,
            ReleaseManifestEndpoints.ForPlatformKey(platformKey),
            platformKey
        );
        var selfUpdate = _selfUpdate;
        _checkTask = _lifecycle.RunAsync(
            lease,
            async cancellationToken =>
            {
                var result = await client.FetchAsync(cancellationToken).ConfigureAwait(false);
                return (
                    Result: result,
                    Evaluation: result.Succeeded
                        ? selfUpdate.Evaluate(BppPluginVersion.Current, result)
                        : null
                );
            },
            ApplyCheck
        );
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

    private void ApplyCheck(
        (ReleaseManifestFetchResult Result, MainMenuSelfUpdateEvaluation? Evaluation) check
    )
    {
        if (check.Evaluation == null)
        {
            if (check.Result.FailureKind != ReleaseManifestFailureKind.Cancelled)
                ReportDegraded(check.Result);
            return;
        }

        _selfUpdate!.Apply(check.Evaluation);
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
