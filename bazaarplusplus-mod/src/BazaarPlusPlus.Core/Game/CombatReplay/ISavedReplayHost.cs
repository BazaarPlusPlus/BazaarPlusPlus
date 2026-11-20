#nullable enable
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>The module calls this port only on the Host thread, inside EnterHostContext.</summary>
public interface ISavedReplayHost
{
    IDisposable EnterHostContext();

    /// <summary>Monotonic realtime seconds.</summary>
    float Now { get; }

    bool InReplayState { get; }

    bool LobbyLoaded { get; }

    bool RecordsVideo { get; }

    bool CanOfferSavedReplay(string battleId);

    void ForgetOffer(string battleId);

    /// <summary>Hosts may defer decoding to BootstrapAsync. A throw from either path must release the playback lease.</summary>
    ISavedReplayStart BeginStart(
        PvpBattleManifest manifest,
        PvpReplayPayload payload,
        IReplayPlaybackOutcomeSink outcome
    );

    /// <summary>Keep the cleanup order specific to the exit path (ADR-0003).</summary>
    ReplayPlaybackCleanupStep[] CleanupSteps(SavedReplayCleanupPath path, string? battleId);

    /// <summary>Throw when the game refuses menu-return dispatch.</summary>
    void ReturnToMainMenu();

    void Notify(in SavedReplayNotice notice);
}

public interface ISavedReplayStart
{
    void Prepare();

    bool NeedsBootstrap { get; }

    /// <summary>True means the scene was bootstrapped from the lobby and playback must return there.</summary>
    Task<bool> BootstrapAsync();

    /// <summary>An opponent preparation failure degrades playback without stopping it.</summary>
    Task PrepareOpponentAsync();

    /// <summary>Throw ReplayPlaybackStartException with StartingPublishFailed if publishStarting fails.</summary>
    Task InjectAsync(Func<ReplayPlaybackPublishOutcome> publishStarting);

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
    Accepted,
    StartBegun,
    StartFailed,
    ExitRequested,
    MenuReturnStarted,
    MenuReturnSettled,

    Completed,
}

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
