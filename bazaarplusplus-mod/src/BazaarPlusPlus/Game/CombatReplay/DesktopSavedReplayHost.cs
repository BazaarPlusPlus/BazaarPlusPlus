#nullable enable
using BazaarPlusPlus.Game.CombatReplay.Bootstrap;
using BazaarPlusPlus.Game.CombatReplay.PlaybackUi;
using BazaarPlusPlus.Game.PvpBattles;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>
/// The desktop's side of Saved Replay Playback (ADR-0020): the Mono loader, bootstrap and
/// opponent portrait, the cleanup each exit path runs with the opening-state restore, and the
/// managed recording UI reset when an operation is accepted or completes.
/// </summary>
internal sealed class DesktopSavedReplayHost : ISavedReplayHost
{
    private readonly SavedReplayCatalog _catalog;
    private readonly OpponentPortraitController _portrait;
    private readonly CombatReplayLoader _loader = new();
    private readonly Action _resetManagedRecordingUi;

    public DesktopSavedReplayHost(
        SavedReplayCatalog catalog,
        OpponentPortraitController portrait,
        Action resetManagedRecordingUi
    )
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
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

    public bool CanOfferSavedReplay(string battleId) => _catalog.HasSavedReplay(battleId);

    public void ForgetOffer(string battleId) { }

    public ISavedReplayStart BeginStart(
        PvpBattleManifest manifest,
        PvpReplayPayload payload,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        var sequence = _loader.Load(payload);
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
    }

    private sealed class NoContext : IDisposable
    {
        public static readonly NoContext Instance = new();

        public void Dispose() { }
    }
}

internal sealed class DesktopSavedReplayStart(
    PvpBattleManifest manifest,
    CombatSequenceMessages sequence,
    IReplayPlaybackOutcomeSink outcome,
    OpponentPortraitController portrait
) : ISavedReplayStart
{
    private ReplayBootstrapContext? _context;

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
        return bootstrappedFromLobby;
    }

    public async Task PrepareOpponentAsync()
    {
        OpponentPortraitController.EnsureOpponentIdentity(manifest, sequence.SpawnMessage, outcome);
        await portrait.EnsureTemporaryOpponentPortraitAsync(manifest, outcome);
    }

    public Task InjectAsync(Func<ReplayPlaybackPublishOutcome> publishStarting) =>
        ReplayBootstrap.InjectSavedReplayAsync(
            _context ?? throw new InvalidOperationException("Replay bootstrap did not resolve."),
            manifest,
            sequence,
            outcome,
            publishStarting
        );

    public Task<ReplayBootstrapRollback> RollbackBootstrapAsync() =>
        ReplayBootstrap.RollbackBootstrapAsync(outcome);
}
