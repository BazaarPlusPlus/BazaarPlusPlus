#nullable enable
using BazaarPlusPlus.Game.CombatReplay.Bootstrap;
using BazaarPlusPlus.Game.CombatReplay.PlaybackUi;
using BazaarPlusPlus.Game.CombatReplay.Warmup;
using BazaarPlusPlus.Game.PvpBattles;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>
/// The desktop's side of Saved Replay Playback (ADR-0020): the Mono loader, bootstrap and
/// opponent portrait, the cleanup each exit path runs with the opening-state restore, History's
/// row probe's short cache, the managed recording UI reset when an operation is accepted or
/// completes, and the release of the board SFX banks only the audio warmup loaded once the
/// operation has settled outside <c>ReplayState</c>.
/// </summary>
internal sealed class DesktopSavedReplayHost : ISavedReplayHost
{
    private readonly SavedReplayOfferCache _offers;
    private readonly OpponentPortraitController _portrait;
    private readonly CombatReplayLoader _loader = new();
    private readonly Action _resetManagedRecordingUi;

    public DesktopSavedReplayHost(
        SavedReplayCatalog catalog,
        OpponentPortraitController portrait,
        Action resetManagedRecordingUi
    )
    {
        if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));
        _offers = new SavedReplayOfferCache(catalog.HasSavedReplay);
        _portrait = portrait ?? throw new ArgumentNullException(nameof(portrait));
        _resetManagedRecordingUi =
            resetManagedRecordingUi
            ?? throw new ArgumentNullException(nameof(resetManagedRecordingUi));
    }

    // Unity calls the desktop on its main thread with no context to install.
    public IDisposable EnterHostContext() => NoContext.Instance;

    public float Now => Time.realtimeSinceStartup;

    public bool InReplayState => AppState.CurrentState is ReplayState;

    public bool LobbyLoaded => SceneLoader.IsSceneLoaded(SceneID.HeroSelectScene);

    public bool RecordsVideo => true;

    // A read failure still reaches History; the shared cache never remembers exceptions.
    public bool CanOfferSavedReplay(string battleId) => _offers.CanOffer(battleId, Now);

    public void ForgetOffer(string battleId) => _offers.Forget(battleId);

    public ISavedReplayStart BeginStart(
        PvpBattleManifest manifest,
        PvpReplayPayload payload,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        // Decoding the three MessagePack parts into game objects is the start's one heavy step, so
        // it runs on a pool thread while the start resets the board and bootstraps; the seal
        // worker already decodes replays off the main thread through the same loader
        // (DesktopPvpBattleEvidence.VerifyReplay). The start awaits it before its first use.
        var sequence = Task.Run(() => _loader.Load(payload));
        // A start that fails before BootstrapAsync never awaits the decode; observe its fault.
        _ = sequence.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        PlaybackUiState.InitializedBoardUiControllers.Clear();
        return new DesktopSavedReplayStart(manifest, sequence, outcome, _portrait);
    }

    public ReplayPlaybackCleanupStep[] CleanupSteps(
        SavedReplayCleanupPath path,
        string? battleId
    ) =>
        path == SavedReplayCleanupPath.StartFailed
            ?
            [
                new("opponent_portrait", () => _portrait.Cleanup(battleId)),
                new("hero_restore", _portrait.RestoreSelectedHeroOverride),
                new("opening_state", ReplayOpeningStateRestorer.Cleanup),
            ]
            :
            [
                new("hero_restore", _portrait.RestoreSelectedHeroOverride),
                new("opponent_portrait", () => _portrait.Cleanup(battleId)),
                new("playback_ui", PlaybackUiState.InitializedBoardUiControllers.Clear),
                new("opening_state", ReplayOpeningStateRestorer.Cleanup),
            ];

    public void ReturnToMainMenu()
    {
        var runManager =
            Services.Get<RunManager>()
            ?? throw new InvalidOperationException("RunManager is unavailable.");
        runManager.ReturnToMainMenu();
    }

    public void Notify(in SavedReplayNotice notice)
    {
        if (notice.Kind is SavedReplayNoticeKind.Accepted or SavedReplayNoticeKind.Completed)
            _resetManagedRecordingUi();
        // Not a cleanup step: on the Exit button the cleanup runs inside the ReplayState.Exit
        // prefix, before the game leaves the state and fades its music. A settled menu return
        // or a completed operation (a failed start included, after its rollback) comes after.
        if (
            notice.Kind
                is SavedReplayNoticeKind.MenuReturnSettled
                    or SavedReplayNoticeKind.Completed
            && !InReplayState
        )
            AudioBankWarmer.ReleaseOwnedBanks(notice.BattleId);
    }

    private sealed class NoContext : IDisposable
    {
        public static readonly NoContext Instance = new();

        public void Dispose() { }
    }
}

internal sealed class DesktopSavedReplayStart(
    PvpBattleManifest manifest,
    Task<CombatSequenceMessages> decode,
    IReplayPlaybackOutcomeSink outcome,
    OpponentPortraitController portrait
) : ISavedReplayStart
{
    private ReplayBootstrapContext? _context;
    private CombatSequenceMessages? _sequence;

    private CombatSequenceMessages Sequence =>
        _sequence ?? throw new InvalidOperationException("The replay payload was not decoded.");

    public void Prepare()
    {
        ReplayOpeningStateRestorer.Cleanup();
        portrait.Cleanup(outcome.BattleId);
        portrait.ApplySelectedHeroOverride(manifest);
        Data.ResetRunData();
    }

    public bool NeedsBootstrap => !ReplayBootstrap.IsBootstrapReady();

    public async Task<bool> BootstrapAsync()
    {
        var bootstrappedFromLobby = await ReplayBootstrap.EnsureBootstrapReadyAsync();
        _context = ReplayBootstrap.ResolveDependencies(outcome);
        // Resumes on the Unity main thread. A payload that does not decode fails the start here,
        // after the bootstrap, through the start-failed cleanup and its rollback.
        _sequence = await decode;
        return bootstrappedFromLobby;
    }

    public async Task PrepareOpponentAsync()
    {
        OpponentPortraitController.EnsureOpponentIdentity(manifest, Sequence.SpawnMessage, outcome);
        await portrait.EnsureTemporaryOpponentPortraitAsync(manifest, outcome);
    }

    public Task InjectAsync(Func<ReplayPlaybackPublishOutcome> publishStarting) =>
        ReplayBootstrap.InjectSavedReplayAsync(
            _context ?? throw new InvalidOperationException("Replay bootstrap did not resolve."),
            manifest,
            Sequence,
            outcome,
            publishStarting
        );

    public Task<ReplayBootstrapRollback> RollbackBootstrapAsync() =>
        ReplayBootstrap.RollbackBootstrapAsync(outcome);
}
