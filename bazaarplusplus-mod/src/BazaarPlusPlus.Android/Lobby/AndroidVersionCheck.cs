using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.SelfUpdate;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.Storage.RunLog;

namespace BazaarPlusPlus.Android.Lobby;

/// <summary>What the game-update row says: the last check's answer beyond the Managed Payload.</summary>
internal enum AndroidGameUpdateStatus
{
    /// <summary>No check has answered yet this session.</summary>
    Checking,

    /// <summary>The last check failed, so whether the game is current is unknown.</summary>
    CheckFailed,

    /// <summary>This copy needs nothing beyond the Managed Payload.</summary>
    Current,

    /// <summary>
    /// The release was adapted for a newer game: the user updates the game from its store
    /// (<see cref="AndroidVersionCheck.Update"/>).
    /// </summary>
    GameUpdated,

    /// <summary>
    /// Same game, but this copy cannot take the release by itself: it needs a new Android App
    /// (<see cref="AndroidVersionCheck.Update"/>).
    /// </summary>
    AppUpdated,
}

/// <summary>
/// The main menu's release check on Android, the counterpart of the desktop's
/// <c>MainMenuVersionCheckController</c>: each main-menu visit reads
/// <c>latest/android-aarch64.json</c>, hands the result to Core's <see cref="MainMenuSelfUpdate"/>,
/// which stages nothing here (the Android App stages the Managed Payload, root ADR 0012) and
/// compares the release with the runtime the App started this process with, and keeps what this
/// copy needs beyond the Managed Payload (<see cref="AndroidGameUpdate.Needed"/>): a newer game
/// or a new Android App. Results land off the main thread; <see cref="Tick"/> observes them on it.
/// </summary>
internal sealed class AndroidVersionCheck : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly ReleaseManifestCheckLifecycle _lifecycle = new();
    private readonly HttpClient _httpClient;
    private readonly ReleaseManifestClient _client;
    private readonly MainMenuSelfUpdate _selfUpdate;
    private readonly string _gameVersion;
    private readonly Uri _manifest;
    private readonly object _gate = new();
    private AndroidUpdate? _update;
    private string? _latestVersion;
    private AndroidGameUpdateStatus _status = AndroidGameUpdateStatus.Checking;
    private Task? _check;

    /// <param name="installRoot">The MelonLoader install root.</param>
    /// <param name="dataRoot">The Data Root.</param>
    /// <param name="gameVersion">The running game's <c>Application.version</c>.</param>
    /// <param name="runtimeDigest">
    /// The runtime digest from the Launch Record (<see cref="Host.AndroidLaunch.RuntimeDigest"/>);
    /// null compares with the installed manifest's.
    /// </param>
    /// <param name="manifestOverride">
    /// A loopback manifest for device tests (<see cref="DevManifest"/>); null reads the published one.
    /// </param>
    public AndroidVersionCheck(
        string installRoot,
        string dataRoot,
        string gameVersion,
        string? runtimeDigest,
        Uri? manifestOverride = null
    )
    {
        _gameVersion = gameVersion ?? string.Empty;
        _manifest =
            manifestOverride
            ?? ReleaseManifestEndpoints.ForPlatformKey(ReleaseManifestEndpoints.AndroidPlatformKey);
        _selfUpdate = CreateSelfUpdate(installRoot, dataRoot, runtimeDigest);
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
    /// The host's self-update: no download transport, since the Android App stages the Managed
    /// Payload, so a payload this copy could take reads "restart from the BPP App"; the running
    /// runtime is the Launch Record's when it names one.
    /// </summary>
    internal static MainMenuSelfUpdate CreateSelfUpdate(
        string installRoot,
        string dataRoot,
        string? runtimeDigest
    ) =>
        new(
            installRoot,
            dataRoot,
            ReleaseManifestEndpoints.AndroidPlatformKey,
            SelfUpdateHost.Android,
            RunLogSchema.LocalDatabaseSchemaVersion,
            downloadClient: null,
            runtimeDigest
        );

    /// <summary>
    /// The developer seam <c>[Dev] ReleaseManifestPort</c>:a port above zero reads the Platform
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

    /// <summary>What this copy needs beyond the Managed Payload, and why; null otherwise.</summary>
    public AndroidUpdate? Update
    {
        get
        {
            lock (_gate)
                return _update;
        }
    }

    /// <summary>The product version the last successful check read; null before one.</summary>
    public string? LatestVersion
    {
        get
        {
            lock (_gate)
                return _latestVersion;
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

        // #753 switches these to the manifest's game and app; the decoder still reads the apk shape.
        var published = check.Result.AndroidApk;
        var update = AndroidGameUpdate.Needed(
            _gameVersion,
            published?.GameVersion,
            published == null ? null : new AndroidAppDownload(published.Url, published.Size),
            check.Evaluation.Decision == SelfUpdateDecision.ManualUpdate
        );
        lock (_gate)
        {
            _update = update;
            _latestVersion = check.Evaluation.LatestVersion;
            _status = update?.Reason switch
            {
                null => AndroidGameUpdateStatus.Current,
                AndroidUpdateReason.GameUpdated => AndroidGameUpdateStatus.GameUpdated,
                _ => AndroidGameUpdateStatus.AppUpdated,
            };
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.game_update.checked"),
            ("game_version", _gameVersion),
            ("published_game_version", published?.GameVersion),
            (
                "decision",
                update?.Reason switch
                {
                    null => "current",
                    AndroidUpdateReason.GameUpdated => "update_game",
                    _ => "install_app",
                }
            ),
            ("manifest", _manifest.IsLoopback ? "dev_loopback" : "published")
        );
        _selfUpdate.Apply(check.Evaluation);
    }

    private void MarkFailed()
    {
        lock (_gate)
        {
            _update = null;
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
