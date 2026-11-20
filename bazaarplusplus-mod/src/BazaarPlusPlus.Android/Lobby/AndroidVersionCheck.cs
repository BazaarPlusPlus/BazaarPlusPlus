using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.SelfUpdate;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.Storage.RunLog;

namespace BazaarPlusPlus.Android.Lobby;

internal enum AndroidGameUpdateStatus
{
    Checking,

    CheckFailed,

    Current,

    NotAdapted,

    GameUpdated,

    AppUpdated,
}

internal sealed class AndroidVersionCheck : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly ReleaseManifestCheckLifecycle _lifecycle = new();
    private readonly HttpClient _httpClient;
    private readonly ReleaseManifestClient _client;
    private readonly MainMenuSelfUpdate _selfUpdate;
    private readonly AndroidGameCode? _runningGame;
    private readonly Uri _manifest;
    private readonly object _gate = new();
    private AndroidUpdate? _update;
    private string? _latestVersion;
    private AndroidGameUpdateStatus _status = AndroidGameUpdateStatus.Checking;
    private AndroidGameAdaptation _adaptation = AndroidGameAdaptation.Unknown;
    private Task? _check;

    public AndroidVersionCheck(
        string installRoot,
        string dataRoot,
        AndroidGameCode? runningGame,
        string? runtimeDigest,
        Uri? manifestOverride = null
    )
    {
        _runningGame = runningGame;
        _manifest =
            manifestOverride
            ?? ReleaseManifestEndpoints.ForPlatformKey(ReleaseManifestEndpoints.AndroidPlatformKey);
        _selfUpdate = CreateSelfUpdate(installRoot, dataRoot, runtimeDigest);
        // Reuse the manifest client across visits so it can retain the last response's ETag for
        // conditional requests.
        _httpClient = CreateClient("VersionCheck", RequestTimeout);
        _client = new ReleaseManifestClient(
            _httpClient,
            _manifest,
            ReleaseManifestEndpoints.AndroidPlatformKey
        );
    }

    // The Android App stages the Managed Payload, so the mod's self-update has no download
    // transport.
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

    // Device tests reach a manifest server on the developer's machine through adb reverse.
    public static Uri? DevManifest(int port) =>
        port is > 0 and <= 65535
            ? new Uri(
                $"http://127.0.0.1:{port}/latest/{ReleaseManifestEndpoints.AndroidPlatformKey}.json"
            )
            : null;

    public Uri Manifest => _manifest;

    public AndroidUpdate? Update
    {
        get
        {
            lock (_gate)
                return _update;
        }
    }

    public string? LatestVersion
    {
        get
        {
            lock (_gate)
                return _latestVersion;
        }
    }

    public AndroidGameUpdateStatus GameUpdateStatus
    {
        get
        {
            lock (_gate)
                return _status;
        }
    }

    public AndroidGameAdaptation Adaptation
    {
        get
        {
            lock (_gate)
                return _adaptation;
        }
    }

    public void Begin()
    {
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

        var published = check.Result.AndroidApp;
        var comparison = AndroidGameUpdate.Compare(_runningGame, published?.Games);
        var adaptation = comparison.Adaptation;
        var update = AndroidGameUpdate.Needed(
            comparison,
            published == null ? null : new AndroidAppDownload(published.AppUrl, published.AppSize),
            check.Evaluation.Decision == SelfUpdateDecision.ManualUpdate
        );
        lock (_gate)
        {
            _update = update;
            _latestVersion = check.Evaluation.LatestVersion;
            _adaptation = adaptation;
            _status = update?.Reason switch
            {
                null when adaptation == AndroidGameAdaptation.NotAdapted =>
                    AndroidGameUpdateStatus.NotAdapted,
                null => AndroidGameUpdateStatus.Current,
                AndroidUpdateReason.GameUpdated => AndroidGameUpdateStatus.GameUpdated,
                _ => AndroidGameUpdateStatus.AppUpdated,
            };
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Lobby, "lobby.game_update.checked"),
            ("game_version_code", _runningGame?.VersionCode),
            ("published_game_source", comparison.Game?.Source),
            ("published_game_version", comparison.Game?.GameVersion),
            ("published_game_version_code", comparison.Game?.VersionCode),
            ("adaptation", adaptation.ToString()),
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
            _adaptation = AndroidGameAdaptation.Unknown;
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
