using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.SelfUpdate;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.Storage.RunLog;

namespace BazaarPlusPlus.Android.Lobby;

/// <summary>What the game-update row says: the last check's answer about the game itself.</summary>
internal enum AndroidGameUpdateStatus
{
    /// <summary>No check has answered yet this session.</summary>
    Checking,

    /// <summary>The last check failed, so whether the game is current is unknown.</summary>
    CheckFailed,

    /// <summary>The published APK is for this game or an older one.</summary>
    Current,

    /// <summary>The published APK is for a newer game (<see cref="AndroidVersionCheck.GameUpdate"/>).</summary>
    Available,
}

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
    private readonly HttpClient _httpClient;
    private readonly ReleaseManifestClient _client;
    private readonly MainMenuSelfUpdate _selfUpdate;
    private readonly string _gameVersion;
    private readonly Uri _manifest;
    private readonly object _gate = new();
    private AndroidApkRelease? _gameUpdate;
    private AndroidGameUpdateStatus _status = AndroidGameUpdateStatus.Checking;
    private Task? _check;

    /// <param name="installRoot">The MelonLoader install root.</param>
    /// <param name="dataRoot">The Data Root.</param>
    /// <param name="gameVersion">The running game's <c>Application.version</c>.</param>
    /// <param name="manifestOverride">
    /// A loopback manifest for device tests (<see cref="DevManifest"/>); null reads the published one.
    /// </param>
    public AndroidVersionCheck(
        string installRoot,
        string dataRoot,
        string gameVersion,
        Uri? manifestOverride = null
    )
    {
        _gameVersion = gameVersion ?? string.Empty;
        _manifest =
            manifestOverride
            ?? ReleaseManifestEndpoints.ForPlatformKey(ReleaseManifestEndpoints.AndroidPlatformKey);
        _selfUpdate = new MainMenuSelfUpdate(
            installRoot,
            dataRoot,
            ReleaseManifestEndpoints.AndroidPlatformKey,
            SelfUpdateHost.Android,
            RunLogSchema.LocalDatabaseSchemaVersion,
            () => CreateClient("SelfUpdate", DownloadTimeout)
        );
        // One client for the process, created last so a throwing constructor leaks none: each
        // visit reuses its pooled connection while it is still open, and the manifest client
        // keeps the last tagged answer for a conditional request.
        _httpClient = CreateClient("VersionCheck", RequestTimeout);
        _client = new ReleaseManifestClient(
            _httpClient,
            _manifest,
            ReleaseManifestEndpoints.AndroidPlatformKey
        );
    }

    /// <summary>
    /// The developer seam <c>[Dev] ReleaseManifestPort</c>: a port above zero reads the Platform
    /// Release Manifest from <c>http://127.0.0.1:&lt;port&gt;/latest/android-aarch64.json</c>, a
    /// server on the developer's machine reached through <c>adb reverse</c>. Zero, the default,
    /// and anything outside the port range read the published manifest.
    /// </summary>
    public static Uri? DevManifest(int port) =>
        port is > 0 and <= 65535
            ? new Uri(
                $"http://127.0.0.1:{port}/latest/{ReleaseManifestEndpoints.AndroidPlatformKey}.json"
            )
            : null;

    /// <summary>The manifest this check reads.</summary>
    public Uri Manifest => _manifest;

    /// <summary>The APK to offer when the published one is for a newer game; null otherwise.</summary>
    public AndroidApkRelease? GameUpdate
    {
        get
        {
            lock (_gate)
                return _gameUpdate;
        }
    }

    /// <summary>
    /// What the last check said about the game. A failed check reads as
    /// <see cref="AndroidGameUpdateStatus.CheckFailed"/>, never as current.
    /// </summary>
    public AndroidGameUpdateStatus GameUpdateStatus
    {
        get
        {
            lock (_gate)
                return _status;
        }
    }

    /// <summary>Starts this main-menu visit's check, replacing one still in flight.</summary>
    public void Begin()
    {
        // The lifecycle only cancels a replaced visit; the shared client outlives every visit.
        var lease = _lifecycle.Begin(NoOwner.Instance);
        var client = _client;
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
        if (check.Exception is not { } exception)
            return;
        MarkFailed();
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

    public void Dispose()
    {
        _lifecycle.Dispose();
        _httpClient.Dispose();
    }

    private void Apply(
        (ReleaseManifestFetchResult Result, MainMenuSelfUpdateEvaluation? Evaluation) check
    )
    {
        if (check.Evaluation == null)
        {
            if (check.Result.FailureKind != ReleaseManifestFailureKind.Cancelled)
            {
                MarkFailed();
                ReportDegraded(check.Result);
            }
            return;
        }

        var gameUpdate = AndroidGameUpdate.Newer(_gameVersion, check.Result.AndroidApk);
        lock (_gate)
        {
            _gameUpdate = gameUpdate;
            _status =
                gameUpdate == null
                    ? AndroidGameUpdateStatus.Current
                    : AndroidGameUpdateStatus.Available;
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.game_update.checked"),
            ("game_version", _gameVersion),
            ("published_game_version", check.Result.AndroidApk?.GameVersion),
            ("decision", gameUpdate == null ? "current" : "download_apk"),
            ("manifest", _manifest.IsLoopback ? "dev_loopback" : "published")
        );
        _selfUpdate.Apply(check.Evaluation);
    }

    private void MarkFailed()
    {
        lock (_gate)
        {
            _gameUpdate = null;
            _status = AndroidGameUpdateStatus.CheckFailed;
        }
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

    private sealed class NoOwner : IDisposable
    {
        public static readonly NoOwner Instance = new();

        public void Dispose() { }
    }
}
