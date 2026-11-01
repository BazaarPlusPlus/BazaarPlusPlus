#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.SelfUpdate;

namespace BazaarPlusPlus.Game.Lobby;

/// <summary>What one release check concluded, computed off the main thread.</summary>
public sealed record MainMenuSelfUpdateEvaluation(
    string LatestVersion,
    SelfUpdateDecision Decision,
    ManagedPayloadRelease? Release
);

/// <summary>
/// The main menu's side of self-update (ADR-0014), shared by every host: it turns a release
/// check into a decision against the running copy, shows it on the version label, and starts
/// the one staging run of the process when the published Managed Payload can be taken. The host
/// supplies its roots, its platform key, and the transport the download owns. A host with no
/// download transport stages nothing itself: on Android the Android App stages the Managed
/// Payload before it starts the game (root ADR 0012), so a payload this copy could take only
/// asks the user to restart from the App.
/// </summary>
public sealed class MainMenuSelfUpdate
{
    private readonly string _installedManifestPath;
    private readonly int _historyDatabaseUserVersion;
    private readonly string? _platformKey;
    private readonly ManagedPayloadStager? _stager;
    private readonly Func<HttpClient>? _downloadClient;
    private readonly string? _runtimeDigest;

    // downloadClient: the transport a staging run downloads with; null when the host stages
    // nothing itself. runtimeDigest: the non-managed payload digest of the runtime this process
    // runs, when a launcher names it (the Android App's Launch Record); null reads the installed
    // manifest's.
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

    /// <summary>Whether this host downloads and stages a Managed Payload itself.</summary>
    public bool StagesManagedPayload => _downloadClient != null;

    /// <summary>
    /// The label status a decision shows; null for <see cref="SelfUpdateDecision.Stage"/> on a
    /// host that stages, where the status follows the staging run.
    /// </summary>
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
