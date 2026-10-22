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
/// supplies its roots, its platform key, and the transport the download owns.
/// </summary>
public sealed class MainMenuSelfUpdate
{
    private readonly string _installedManifestPath;
    private readonly int _historyDatabaseUserVersion;
    private readonly string? _platformKey;
    private readonly ManagedPayloadStager? _stager;
    private readonly Func<HttpClient> _downloadClient;

    public MainMenuSelfUpdate(
        string installRoot,
        string dataRoot,
        string? platformKey,
        SelfUpdateHost host,
        int historyDatabaseUserVersion,
        Func<HttpClient> downloadClient
    )
    {
        _installedManifestPath = SelfUpdatePaths.InstalledManifest(installRoot, host);
        _historyDatabaseUserVersion = historyDatabaseUserVersion;
        _platformKey = platformKey;
        _stager =
            platformKey == null
                ? null
                : new ManagedPayloadStager(SelfUpdatePaths.Updates(dataRoot), platformKey, host);
        _downloadClient = downloadClient ?? throw new ArgumentNullException(nameof(downloadClient));
    }

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
            _historyDatabaseUserVersion
        );
        var release = _stager == null ? null : result.ManagedPayload;
        var decision = SelfUpdatePolicy.Decide(
            running,
            latestVersion,
            release,
            staged: _stager?.IsStaged(latestVersion) == true
        );
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.SelfUpdate, "self_update.checked"),
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
        switch (evaluation.Decision)
        {
            case SelfUpdateDecision.UpToDate:
                MainMenuVersionUpdateState.SetStatus(MainMenuUpdateStatus.None);
                return;
            case SelfUpdateDecision.ManualUpdate:
                MainMenuVersionUpdateState.SetStatus(MainMenuUpdateStatus.ManualUpdate);
                return;
            case SelfUpdateDecision.Staged:
                MainMenuVersionUpdateState.SetStatus(MainMenuUpdateStatus.RestartToUpdate);
                return;
        }

        MainMenuVersionUpdateState.SetStatus(MainMenuUpdateStatus.Updating);
        var stager = _stager!;
        var release = evaluation.Release!;
        SelfUpdateStaging
            .Ensure(release.Version, () => StageAsync(stager, release))
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

    private async Task<ManagedPayloadStageResult> StageAsync(
        ManagedPayloadStager stager,
        ManagedPayloadRelease release
    )
    {
        ManagedPayloadStageResult result;
        try
        {
            using var httpClient = _downloadClient();
            result = await stager
                .StageAsync(httpClient, release, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.SelfUpdate, "self_update.stage_failed"),
                exception,
                ("version", release.Version),
                ("reason_code", "exception")
            );
            return ManagedPayloadStageResult.Failed("exception");
        }

        if (result.Succeeded)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.SelfUpdate, "self_update.staged"),
                ("version", release.Version),
                ("size", release.Size)
            );
        else
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.SelfUpdate, "self_update.stage_failed"),
                ("version", release.Version),
                ("reason_code", result.ReasonCode)
            );
        return result;
    }
}
