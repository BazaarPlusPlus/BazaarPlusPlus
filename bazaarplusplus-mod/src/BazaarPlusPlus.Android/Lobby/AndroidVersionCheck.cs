using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.SelfUpdate;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.Storage.RunLog;

namespace BazaarPlusPlus.Android.Lobby;

/// <summary>
/// The main menu's release check on Android, the counterpart of the desktop's
/// <c>MainMenuVersionCheckController</c>: each main-menu visit reads
/// <c>latest/android-aarch64.json</c>, hands the result to Core's <see cref="MainMenuSelfUpdate"/>
/// (which stages a newer Managed Payload for the Updater), and keeps the Android Release APK to
/// offer when the game itself is behind it. Results land off the main thread; <see cref="Tick"/>
/// observes them on it.
/// </summary>
internal sealed class AndroidVersionCheck : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    private readonly ReleaseManifestCheckLifecycle _lifecycle = new();
    private readonly MainMenuSelfUpdate _selfUpdate;
    private readonly string _gameVersion;
    private readonly object _gate = new();
    private AndroidApkRelease? _gameUpdate;
    private Task? _check;

    public AndroidVersionCheck(string installRoot, string dataRoot, string gameVersion)
    {
        _gameVersion = gameVersion ?? string.Empty;
        _selfUpdate = new MainMenuSelfUpdate(
            installRoot,
            dataRoot,
            ReleaseManifestEndpoints.AndroidPlatformKey,
            SelfUpdateHost.Android,
            RunLogSchema.LocalDatabaseSchemaVersion,
            () => CreateClient("SelfUpdate", DownloadTimeout)
        );
    }

    /// <summary>The APK to offer when the published one is for a newer game; null otherwise.</summary>
    public AndroidApkRelease? GameUpdate
    {
        get
        {
            lock (_gate)
                return _gameUpdate;
        }
    }

    /// <summary>Starts this main-menu visit's check, replacing one still in flight.</summary>
    public void Begin()
    {
        var httpClient = CreateClient("VersionCheck", RequestTimeout);
        var lease = _lifecycle.Begin(httpClient);
        var client = new ReleaseManifestClient(
            httpClient,
            ReleaseManifestEndpoints.ForPlatformKey(ReleaseManifestEndpoints.AndroidPlatformKey),
            ReleaseManifestEndpoints.AndroidPlatformKey
        );
        var selfUpdate = _selfUpdate;
        _check = _lifecycle.RunAsync(
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
            Apply
        );
    }

    /// <summary>Main thread: surfaces a check that ended in a fault instead of dropping it.</summary>
    public void Tick()
    {
        var check = _check;
        if (check is not { IsCompleted: true })
            return;
        _check = null;
        if (check.Exception is { } exception)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Lobby,
                    "lobby.version_check.degraded",
                    storm: ["reason_code"]
                ),
                exception,
                ("reason_code", LobbyLogReasonCode.RequestException)
            );
    }

    public void Dispose() => _lifecycle.Dispose();

    private void Apply(
        (ReleaseManifestFetchResult Result, MainMenuSelfUpdateEvaluation? Evaluation) check
    )
    {
        if (check.Evaluation == null)
        {
            if (check.Result.FailureKind != ReleaseManifestFailureKind.Cancelled)
                ReportDegraded(check.Result);
            return;
        }

        var gameUpdate = AndroidGameUpdate.Newer(_gameVersion, check.Result.AndroidApk);
        lock (_gate)
            _gameUpdate = gameUpdate;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.game_update.checked"),
            ("game_version", _gameVersion),
            ("published_game_version", check.Result.AndroidApk?.GameVersion),
            ("decision", gameUpdate == null ? "current" : "download_apk")
        );
        _selfUpdate.Apply(check.Evaluation);
    }

    private static HttpClient CreateClient(string userAgentSuffix, TimeSpan timeout) =>
        BppHttpClientFactory.Create(
            productVersion: BppPluginVersion.Current,
            platformKey: ReleaseManifestEndpoints.AndroidPlatformKey,
            userAgentSuffix: userAgentSuffix,
            timeout: timeout
        );

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
        var logEvent = new BppLogEvent(
            BppLogFeatureScope.Lobby,
            "lobby.version_check.degraded",
            storm: ["reason_code"]
        );
        BppLogField[] fields =
        [
            ("reason_code", reasonCode),
            ("http_status", result.HttpStatus),
            ("platform", ReleaseManifestEndpoints.AndroidPlatformKey),
        ];
        if (result.Exception == null)
            BppLog.WarnEvent(logEvent, fields);
        else
            BppLog.WarnEvent(logEvent, result.Exception, fields);
    }
}
