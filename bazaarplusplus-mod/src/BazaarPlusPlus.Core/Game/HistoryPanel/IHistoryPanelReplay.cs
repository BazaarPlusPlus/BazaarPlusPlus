#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;

namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>
/// The replay runtime History's coordinator drives: whether a battle can replay or record, and
/// the replay itself. Each host implements it over its own combat replay runtime.
/// </summary>
public interface IHistoryPanelReplay
{
    bool CanReplayBattle(HistoryBattleRecord? battle, out string reason);

    bool CanRecordReplay(HistoryBattleRecord? battle, out string reason);

    bool CanRecordReplay(
        HistoryBattleRecord? battle,
        out string reason,
        out HistoryPanelReplayReasonCode reasonCode
    );

    // The recording half of CanRecordReplay, for a replay whose availability the caller checked
    // (a favorite replays from its own copy, not from the battle's saved payload).
    bool CanRecordLoadedReplay(out string reason);

    bool CanRecordLoadedReplay(out string reason, out HistoryPanelReplayReasonCode reasonCode);

    // Runs on the UI thread, where the host can read its video settings.
    void PrewarmRecordingAvailability();

    // canCommit is checked right before the replay starts; false cancels it.
    Task<HistoryPanelReplayAttemptResult> ReplayBattleAsync(
        HistoryBattleRecord? battle,
        bool recordVideo,
        CancellationToken cancellationToken,
        Func<bool>? canCommit = null
    );

    ReplayPayloadCleanupResult CleanupReplayPayloads(IReadOnlyList<string> battleIds);

    string GetReplayActionLabel(HistoryBattleRecord? battle);
}

public readonly struct HistoryPanelReplayAttemptResult
{
    private HistoryPanelReplayAttemptResult(
        bool succeeded,
        string statusMessage,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception
    )
    {
        Succeeded = succeeded;
        StatusMessage = statusMessage;
        ReasonCode = reasonCode;
        Exception = exception;
    }

    public bool Succeeded { get; }

    public string StatusMessage { get; }
    public HistoryPanelReplayReasonCode ReasonCode { get; }
    public Exception? Exception { get; }

    public static HistoryPanelReplayAttemptResult Success(string statusMessage) =>
        new(true, statusMessage, HistoryPanelReplayReasonCode.Completed, null);

    public static HistoryPanelReplayAttemptResult Failure(
        string statusMessage,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception = null
    ) => new(false, statusMessage, reasonCode, exception);
}

// A Ghost replay loaded for playback or a favorite copy. Payload is null exactly when Failure
// says why.
public readonly record struct GhostReplayLoad(
    GhostBattlePayload? Payload,
    HistoryPanelReplayAttemptResult Failure
)
{
    public static GhostReplayLoad Failed(
        string message,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception = null
    ) => new(null, HistoryPanelReplayAttemptResult.Failure(message, reasonCode, exception));
}
