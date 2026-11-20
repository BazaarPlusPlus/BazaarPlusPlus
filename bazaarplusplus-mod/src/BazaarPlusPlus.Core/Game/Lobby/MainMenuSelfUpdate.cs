#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.SelfUpdate;

namespace BazaarPlusPlus.Game.Lobby;

public sealed record MainMenuSelfUpdateEvaluation(
    string LatestVersion,
    SelfUpdateDecision Decision,
    ManagedPayloadRelease? Release
);

// The Android App stages the Managed Payload before launch, so that host can only prompt for a restart.
public sealed class MainMenuSelfUpdate
{
    private readonly string _installedManifestPath;
    private readonly int _historyDatabaseUserVersion;
    private readonly string? _platformKey;
    private readonly ManagedPayloadStager? _stager;
    private readonly Func<HttpClient>? _downloadClient;
    private readonly string? _runtimeDigest;

    // The Launch Record can override the installed manifest's runtime digest when a launcher supplies the runtime.
    public MainMenuSelfUpdate(
        string installRoot,
        string dataRoot,
        string? platformKey,
        SelfUpdateHost host,
        int historyDatabaseUserVersion,
        Func<HttpClient>? downloadClient,
        string? runtimeDigest = null
    )
    {
        _installedManifestPath = SelfUpdatePaths.InstalledManifest(installRoot, host);
        _historyDatabaseUserVersion = historyDatabaseUserVersion;
        _platformKey = platformKey;
        _stager =
            platformKey == null
                ? null
                : new ManagedPayloadStager(SelfUpdatePaths.Updates(dataRoot), platformKey, host);
        _downloadClient = downloadClient;
        _runtimeDigest = runtimeDigest;
    }

    public bool StagesManagedPayload => _downloadClient != null;

    public static MainMenuUpdateStatus? StatusFor(SelfUpdateDecision decision, bool hostStages) =>
        decision switch
        {
            SelfUpdateDecision.UpToDate => MainMenuUpdateStatus.None,
            SelfUpdateDecision.ManualUpdate => MainMenuUpdateStatus.ManualUpdate,
            SelfUpdateDecision.Staged => MainMenuUpdateStatus.RestartToUpdate,
            _ => hostStages ? null : MainMenuUpdateStatus.RestartFromApp,
        };

    public MainMenuSelfUpdateEvaluation Evaluate(
        string runningVersion,
        ReleaseManifestFetchResult result
    )
    {
        var latestVersion =
            result.Version ?? throw new ArgumentException("A successful check.", nameof(result));
        var running = RunningManagedPayload.Read(
            _installedManifestPath,
            runningVersion,
            _historyDatabaseUserVersion,
            _runtimeDigest
        );
        var release = _stager == null ? null : result.ManagedPayload;
        var decision = SelfUpdatePolicy.Decide(
            running,
            latestVersion,
            release,
            staged: _stager?.IsStaged(latestVersion) == true
        );
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.SelfUpdate, "self_update.release.checked"),
            ("current_version", runningVersion),
            ("latest_version", latestVersion),
            ("decision", decision),
            ("platform", _platformKey),
            ("running_digest", running.NonManagedPayloadDigest == null ? "none" : "present")
        );
        return new MainMenuSelfUpdateEvaluation(latestVersion, decision, release);
    }

    public void Apply(MainMenuSelfUpdateEvaluation evaluation)
    {
        if (evaluation == null)
            throw new ArgumentNullException(nameof(evaluation));
        if (StatusFor(evaluation.Decision, StagesManagedPayload) is { } status)
        {
            MainMenuVersionUpdateState.SetStatus(status);
            return;
        }

        MainMenuVersionUpdateState.SetStatus(MainMenuUpdateStatus.Updating);
        var stager = _stager!;
        var release = evaluation.Release!;
        var downloadClient = _downloadClient!;
        SelfUpdateStaging
            .Ensure(release.Version, () => StageAsync(stager, release, downloadClient))
            .ContinueWith(
                run =>
                    MainMenuVersionUpdateState.SetStatus(
                        run.Status == TaskStatus.RanToCompletion && run.Result.Succeeded
                            ? MainMenuUpdateStatus.RestartToUpdate
                            : MainMenuUpdateStatus.ManualUpdate
                    ),
                TaskScheduler.Default
            );
    }

    private static async Task<ManagedPayloadStageResult> StageAsync(
        ManagedPayloadStager stager,
        ManagedPayloadRelease release,
        Func<HttpClient> downloadClient
    )
    {
        ManagedPayloadStageResult result;
        try
        {
            using var httpClient = downloadClient();
            result = await stager
                .StageAsync(httpClient, release, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.SelfUpdate, "self_update.payload.stage_failed"),
                exception,
                ("version", release.Version),
                ("reason_code", "exception")
            );
            return ManagedPayloadStageResult.Failed("exception");
        }

        if (result.Succeeded)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.SelfUpdate, "self_update.payload.staged"),
                ("version", release.Version),
                ("size", release.Size)
            );
        else
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.SelfUpdate, "self_update.payload.stage_failed"),
                ("version", release.Version),
                ("reason_code", result.ReasonCode)
            );
        return result;
    }
}
