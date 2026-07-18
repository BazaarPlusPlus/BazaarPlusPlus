#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;

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
    private readonly bool _recordVideo;
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

    internal bool IsTerminal
    {
        get
        {
            lock (_gate)
                return _terminal;
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
            CombatReplayLogEvents.PlaybackStarted,
            CombatReplayLogEvents.PlaybackStartedBattleId.Bind(result.BattleId),
            CombatReplayLogEvents.PlaybackStartedSource.Bind(result.Source),
            CombatReplayLogEvents.PlaybackStartedRecordVideo.Bind(result.RecordVideo)
        );
    }

    internal static void EmitTerminal(ReplayPlaybackTerminalResult result)
    {
        var fields = new[]
        {
            CombatReplayLogEvents.PlaybackTerminalBattleId.Bind(result.BattleId),
            CombatReplayLogEvents.PlaybackTerminalSource.Bind(result.Source),
            CombatReplayLogEvents.PlaybackTerminalEndReasonCode.Bind(result.EndReasonCode),
            CombatReplayLogEvents.PlaybackTerminalDurationMs.Bind(result.DurationMilliseconds),
            CombatReplayLogEvents.PlaybackTerminalReasonCode.Bind(result.ReasonCode),
            CombatReplayLogEvents.PlaybackTerminalDegradationCount.Bind(result.DegradationCount),
            CombatReplayLogEvents.PlaybackTerminalRollbackStatus.Bind(result.RollbackStatus),
        };

        switch (result.Status)
        {
            case ReplayPlaybackTerminalStatus.Succeeded:
                BppLog.InfoEvent(CombatReplayLogEvents.PlaybackSucceeded, fields);
                return;
            case ReplayPlaybackTerminalStatus.Degraded:
                if (result.Exception == null)
                    BppLog.WarnEvent(CombatReplayLogEvents.PlaybackDegraded, fields);
                else
                    BppLog.WarnEvent(
                        CombatReplayLogEvents.PlaybackDegraded,
                        result.Exception,
                        fields
                    );
                return;
            case ReplayPlaybackTerminalStatus.Failed:
                if (result.Exception == null)
                    BppLog.ErrorEvent(CombatReplayLogEvents.PlaybackFailed, fields);
                else
                    BppLog.ErrorEvent(
                        CombatReplayLogEvents.PlaybackFailed,
                        result.Exception,
                        fields
                    );
                return;
        }
    }
}
