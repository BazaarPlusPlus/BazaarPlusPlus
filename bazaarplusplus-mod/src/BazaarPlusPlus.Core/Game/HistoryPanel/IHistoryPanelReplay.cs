#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;

namespace BazaarPlusPlus.Game.HistoryPanel;

public interface IHistoryPanelReplay
{
    bool CanReplayBattle(HistoryBattleRecord? battle, out string reason);

    bool CanRecordReplay(
        HistoryBattleRecord? battle,
        out string reason,
        out HistoryPanelReplayReasonCode reasonCode
    );

    bool CanRecordLoadedReplay(out string reason);

    bool CanRecordLoadedReplay(out string reason, out HistoryPanelReplayReasonCode reasonCode);

    bool RecordingSupported { get; }

    // Runs on the UI thread, where the host can read its video settings.
    void PrewarmRecordingAvailability();

    // canCommit is checked right before the replay starts; false cancels it.
    Task<HistoryPanelReplayAttemptResult> ReplayBattleAsync(
        HistoryBattleRecord? battle,
        bool recordVideo,
        CancellationToken cancellationToken,
        Func<bool>? canCommit = null
    );

    // Capture the runtime on the UI thread. Invoke the returned factory only on a worker;
    // a null lease refuses deletion before its database transaction starts.
    Func<IDisposable?> PreparePayloadMaintenance();

    // The caller holds the prepared maintenance lease over both the DB delete and this cleanup.
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
