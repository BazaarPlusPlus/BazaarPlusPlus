#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatReplay;

public interface IReplayPlaybackOutcomeSink
{
    string BattleId { get; }
    void ReportDegradation(ReplayPlaybackReasonCode reasonCode, Exception? exception = null);
}

/// <summary>The ended signal can precede menu navigation. Delay the terminal log until the required cleanup has settled.</summary>
public sealed class ReplayPlaybackLogOperation : IReplayPlaybackOutcomeSink
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

    public ReplayPlaybackLogOperation(
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
    public bool RecordVideo
    {
        get
        {
            lock (_gate)
                return _recordVideo;
        }
    }

    public bool TryPromoteToRecording()
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

    public void MarkStarted()
    {
        bool recordVideo;
        lock (_gate)
        {
            if (_terminal || _started)
                return;

            _started = true;
            recordVideo = _recordVideo;
        }

        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.CombatReplay, "combat_replay.playback.started"),
            ("battle_id", BattleId, BppLogCorrelationPolicy.Short),
            ("source", _source),
            ("record_video", recordVideo)
        );
    }

    public void Complete(
        ReplayPlaybackEndReasonCode endReasonCode,
        ReplayRollbackStatus rollbackStatus,
        ReplayPlaybackReasonCode failureReasonCode,
        Exception? exception
    )
    {
        bool failed;
        int degradationCount;
        ReplayPlaybackReasonCode reasonCode;
        Exception? terminalException;
        long durationMilliseconds;
        lock (_gate)
        {
            if (_terminal)
                return;

            _terminal = true;
            failed =
                failureReasonCode != ReplayPlaybackReasonCode.None
                || rollbackStatus == ReplayRollbackStatus.Failed;
            degradationCount = _degradationCount;
            reasonCode = failed
                ? failureReasonCode == ReplayPlaybackReasonCode.None
                    ? ReplayPlaybackReasonCode.BootstrapRollbackFailed
                    : failureReasonCode
                : _primaryReasonCode;
            terminalException = failed ? exception : _primaryException;
            durationMilliseconds = Math.Max(0, _monotonicMilliseconds() - _startedAtMilliseconds);
        }

        var fields = new BppLogField[]
        {
            ("battle_id", BattleId, BppLogCorrelationPolicy.Short),
            ("source", _source),
            ("end_reason_code", endReasonCode),
            ("duration_ms", durationMilliseconds),
            ("reason_code", reasonCode),
            ("degradation_count", degradationCount),
            ("rollback_status", rollbackStatus),
        };
        if (failed)
        {
            var failedEvent = new BppLogEvent(
                BppLogFeatureScope.CombatReplay,
                "combat_replay.playback.failed"
            );
            if (terminalException == null)
                BppLog.ErrorEvent(failedEvent, fields);
            else
                BppLog.ErrorEvent(failedEvent, terminalException, fields);
        }
        else if (degradationCount > 0)
        {
            var degradedEvent = new BppLogEvent(
                BppLogFeatureScope.CombatReplay,
                "combat_replay.playback.degraded"
            );
            if (terminalException == null)
                BppLog.WarnEvent(degradedEvent, fields);
            else
                BppLog.WarnEvent(degradedEvent, terminalException, fields);
        }
        else
        {
            BppLog.InfoEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CombatReplay,
                    "combat_replay.playback.succeeded"
                ),
                fields
            );
        }
    }

    private static long MonotonicMilliseconds() =>
        (long)(Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency);
}
