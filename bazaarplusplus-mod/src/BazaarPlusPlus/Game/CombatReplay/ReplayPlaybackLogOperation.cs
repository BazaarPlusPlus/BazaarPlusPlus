#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatReplay;

internal interface IReplayPlaybackOutcomeSink
{
    string BattleId { get; }
    void ReportDegradation(ReplayPlaybackReasonCode reasonCode, Exception? exception = null);
}

internal enum ReplayPlaybackTerminalStatus
{
    Succeeded,
    Degraded,
    Failed,
}

internal readonly record struct ReplayPlaybackStartedResult(
    string BattleId,
    CombatReplayPlaybackSource Source,
    bool RecordVideo
);

internal readonly record struct ReplayPlaybackTerminalResult(
    ReplayPlaybackTerminalStatus Status,
    string BattleId,
    CombatReplayPlaybackSource Source,
    ReplayPlaybackEndReasonCode EndReasonCode,
    long DurationMilliseconds,
    ReplayPlaybackReasonCode ReasonCode,
    int DegradationCount,
    ReplayRollbackStatus RollbackStatus,
    Exception? Exception
);

/// <summary>
/// Thread-safe, one-shot operational result for one requested replay. Lifecycle events remain
/// separate: the runtime may publish the ended signal before menu navigation resolves, but this
/// operation does not emit its terminal result until all required cleanup is known.
/// </summary>
internal sealed class ReplayPlaybackLogOperation : IReplayPlaybackOutcomeSink
{
    private readonly object _gate = new();
    private readonly Func<long> _monotonicMilliseconds;
    private readonly long _startedAtMilliseconds;
    private readonly CombatReplayPlaybackSource _source;
    private bool _recordVideo;
    private bool _started;
    private bool _terminal;
    private int _degradationCount;
    private ReplayPlaybackReasonCode _primaryReasonCode;
    private Exception? _primaryException;

    internal ReplayPlaybackLogOperation(
        string battleId,
        CombatReplayPlaybackSource source,
        bool recordVideo,
        Func<long>? monotonicMilliseconds = null
    )
    {
        BattleId = string.IsNullOrWhiteSpace(battleId) ? string.Empty : battleId.Trim();
        _source = source;
        _recordVideo = recordVideo;
        _monotonicMilliseconds = monotonicMilliseconds ?? MonotonicMilliseconds;
        _startedAtMilliseconds = _monotonicMilliseconds();
    }

    public string BattleId { get; }
    internal bool RecordVideo
    {
        get
        {
            lock (_gate)
                return _recordVideo;
        }
    }

    internal bool TryPromoteToRecording()
    {
        lock (_gate)
        {
            if (_terminal)
                return false;

            _recordVideo = true;
            return true;
        }
    }

    public void ReportDegradation(ReplayPlaybackReasonCode reasonCode, Exception? exception = null)
    {
        if (reasonCode == ReplayPlaybackReasonCode.None)
            return;

        lock (_gate)
        {
            if (_terminal)
                return;

            _degradationCount++;
            if (_primaryReasonCode == ReplayPlaybackReasonCode.None)
            {
                _primaryReasonCode = reasonCode;
                _primaryException = exception;
            }
        }
    }

    internal bool TryMarkStarted(out ReplayPlaybackStartedResult result)
    {
        lock (_gate)
        {
            if (_terminal || _started)
            {
                result = default;
                return false;
            }

            _started = true;
            result = new ReplayPlaybackStartedResult(BattleId, _source, _recordVideo);
            return true;
        }
    }

    internal bool TryComplete(
        ReplayPlaybackEndReasonCode endReasonCode,
        ReplayRollbackStatus rollbackStatus,
        ReplayPlaybackReasonCode failureReasonCode,
        Exception? exception,
        out ReplayPlaybackTerminalResult result
    )
    {
        lock (_gate)
        {
            if (_terminal)
            {
                result = default;
                return false;
            }

            _terminal = true;
            var failed =
                failureReasonCode != ReplayPlaybackReasonCode.None
                || rollbackStatus == ReplayRollbackStatus.Failed;
            var status =
                failed ? ReplayPlaybackTerminalStatus.Failed
                : _degradationCount > 0 ? ReplayPlaybackTerminalStatus.Degraded
                : ReplayPlaybackTerminalStatus.Succeeded;
            var reasonCode = failed
                ? failureReasonCode == ReplayPlaybackReasonCode.None
                    ? ReplayPlaybackReasonCode.BootstrapRollbackFailed
                    : failureReasonCode
                : _primaryReasonCode;
            var terminalException = failed ? exception : _primaryException;
            result = new ReplayPlaybackTerminalResult(
                status,
                BattleId,
                _source,
                endReasonCode,
                Math.Max(0, _monotonicMilliseconds() - _startedAtMilliseconds),
                reasonCode,
                _degradationCount,
                rollbackStatus,
                terminalException
            );
            return true;
        }
    }

    private static long MonotonicMilliseconds() =>
        (long)(Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency);
}

internal static class ReplayPlaybackLogWriter
{
    internal static void EmitStarted(ReplayPlaybackStartedResult result)
    {
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.CombatReplay, "combat_replay.playback.started"),
            ("battle_id", result.BattleId, BppLogCorrelationPolicy.Short),
            ("source", result.Source),
            ("record_video", result.RecordVideo)
        );
    }

    internal static void EmitTerminal(ReplayPlaybackTerminalResult result)
    {
        var fields = new BppLogField[]
        {
            ("battle_id", result.BattleId, BppLogCorrelationPolicy.Short),
            ("source", result.Source),
            ("end_reason_code", result.EndReasonCode),
            ("duration_ms", result.DurationMilliseconds),
            ("reason_code", result.ReasonCode),
            ("degradation_count", result.DegradationCount),
            ("rollback_status", result.RollbackStatus),
        };

        switch (result.Status)
        {
            case ReplayPlaybackTerminalStatus.Succeeded:
                BppLog.InfoEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CombatReplay,
                        "combat_replay.playback.succeeded"
                    ),
                    fields
                );
                return;
            case ReplayPlaybackTerminalStatus.Degraded:
                if (result.Exception == null)
                    BppLog.WarnEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.CombatReplay,
                            "combat_replay.playback.degraded"
                        ),
                        fields
                    );
                else
                    BppLog.WarnEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.CombatReplay,
                            "combat_replay.playback.degraded"
                        ),
                        result.Exception,
                        fields
                    );
                return;
            case ReplayPlaybackTerminalStatus.Failed:
                if (result.Exception == null)
                    BppLog.ErrorEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.CombatReplay,
                            "combat_replay.playback.failed"
                        ),
                        fields
                    );
                else
                    BppLog.ErrorEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.CombatReplay,
                            "combat_replay.playback.failed"
                        ),
                        result.Exception,
                        fields
                    );
                return;
        }
    }
}
