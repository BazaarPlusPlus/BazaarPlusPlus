#nullable enable
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>
/// One host's side of <see cref="SavedReplayPlayback"/> (ADR-0020): the game reads, the clock,
/// the cleanup each exit path runs, the menu return and the notices a host acts on. The module
/// calls it on the host's main thread only, always inside <see cref="EnterHostContext"/>.
/// </summary>
public interface ISavedReplayHost
{
    /// <summary>The scope every module entry point runs in; the Android host's main-thread pump.</summary>
    IDisposable EnterHostContext();

    /// <summary>Realtime seconds, the clock of the exit window and the menu-return deadline.</summary>
    float Now { get; }

    bool InReplayState { get; }

    /// <summary>The lobby a menu return lands in has loaded; read only while one is pending.</summary>
    bool LobbyLoaded { get; }

    /// <summary>False on a host that records no video: every replay starts without recording.</summary>
    bool RecordsVideo { get; }

    /// <summary>
    /// History's per-row probe of whether a saved battle can replay. A host may answer from a
    /// short cache; <see cref="SavedReplayPlayback.ReplaySaved"/> always rechecks the catalog.
    /// </summary>
    bool CanOfferSavedReplay(string battleId);

    /// <summary>Drops whatever <see cref="CanOfferSavedReplay"/> remembers for the battle.</summary>
    void ForgetOffer(string battleId);

    /// <summary>
    /// Turns the payload into the game's messages and clears the board UI. A throw is a clean
    /// start failure: the module completes the operation and frees the lease.
    /// </summary>
    ISavedReplayStart BeginStart(
        PvpBattleManifest manifest,
        PvpReplayPayload payload,
        IReplayPlaybackOutcomeSink outcome
    );

    /// <summary>The cleanup of one exit path, in its own order (ADR-0003).</summary>
    ReplayPlaybackCleanupStep[] CleanupSteps(SavedReplayCleanupPath path, string? battleId);

    /// <summary>Dispatches the game's return to the main menu; throws when dispatch fails.</summary>
    void ReturnToMainMenu();

    void Notify(in SavedReplayNotice notice);
}

/// <summary>
/// One start's host objects (the game's combat sequence, the bootstrap context, the opponent),
/// created by <see cref="ISavedReplayHost.BeginStart"/> and dropped when the start settles.
/// </summary>
public interface ISavedReplayStart
{
    /// <summary>Resets the board for the start: opponent cleanup, hero override, run data.</summary>
    void Prepare();

    /// <summary>The game scene is not bootstrapped, so the start bootstraps it from the lobby.</summary>
    bool NeedsBootstrap { get; }

    /// <summary>
    /// Ensures the game scene and resolves the injection dependencies; true when it bootstrapped
    /// from the lobby and the replay must return there. A throw is a start failure.
    /// </summary>
    Task<bool> BootstrapAsync();

    /// <summary>The opponent's identity and portrait; a throw only degrades the replay.</summary>
    Task PrepareOpponentAsync();

    /// <summary>
    /// Injects the battle and plays it, publishing the starting signal through
    /// <paramref name="publishStarting"/>; a failed publish throws
    /// <see cref="ReplayPlaybackStartException"/> with <see cref="ReplayPlaybackReasonCode.StartingPublishFailed"/>.
    /// </summary>
    Task InjectAsync(Func<ReplayPlaybackPublishOutcome> publishStarting);

    /// <summary>Returns a failed lobby bootstrap to the lobby; reports a failure, never throws.</summary>
    Task<ReplayBootstrapRollback> RollbackBootstrapAsync();
}

public enum SavedReplayCleanupPath
{
    StartFailed,
    StateExit,
    BootstrappedExit,
}

public enum SavedReplayNoticeKind
{
    /// <summary>A replay request passed admission and its operation is now the active one.</summary>
    Accepted,
    StartBegun,
    StartFailed,
    ExitRequested,
    MenuReturnStarted,
    MenuReturnSettled,

    /// <summary>The active operation wrote its terminal event and released its lease.</summary>
    Completed,
}

/// <summary>The answer to a bootstrapped <c>ReplayState.Exit</c> request; names as logged.</summary>
public enum SavedReplayExitOutcome
{
    Suppressed,
    NotActive,
    Proceed,
}

public readonly record struct SavedReplayNotice(
    SavedReplayNoticeKind Kind,
    string? BattleId,
    CombatReplayPlaybackSource? Source = null,
    SavedReplayExitOutcome? ExitOutcome = null,
    ReplayPlaybackEndReasonCode? EndReason = null,
    bool TimedOut = false
);

public sealed record ReplayBootstrapRollback(bool Succeeded, Exception? Exception)
{
    public static readonly ReplayBootstrapRollback Success = new(true, null);

    public static ReplayBootstrapRollback Failure(Exception exception) =>
        new(false, exception ?? throw new ArgumentNullException(nameof(exception)));
}

/// <summary>
/// A replay start that stopped with a known reason: the starting publish failed, or
/// <c>ReplayState</c> exited before the start committed.
/// </summary>
public sealed class ReplayPlaybackStartException : Exception
{
    public ReplayPlaybackStartException(
        ReplayPlaybackReasonCode reasonCode,
        Exception? innerException
    )
        : base(MessageFor(reasonCode), innerException)
    {
        ReasonCode = reasonCode;
    }

    public ReplayPlaybackReasonCode ReasonCode { get; }

    private static string MessageFor(ReplayPlaybackReasonCode reasonCode) =>
        reasonCode == ReplayPlaybackReasonCode.StartingPublishFailed
            ? "Replay lifecycle event publication failed."
            : "ReplayState exited before replay startup completed.";
}
