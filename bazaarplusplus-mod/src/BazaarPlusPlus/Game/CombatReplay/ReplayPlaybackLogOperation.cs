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

/// <summary>
/// Thread-safe, one-shot operational result for one requested replay: it owns the recording
/// promotion decision and writes the started and terminal events. Lifecycle events remain
/// separate: the runtime may publish the ended signal before menu navigation resolves, but this
/// operation does not write its terminal event until all required cleanup is known.
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

    /// <summary>Writes <c>combat_replay.playback.started</c> once, unless already terminal.</summary>
    internal void MarkStarted()
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

    /// <summary>
    /// Writes the one terminal event: <c>failed</c> for a failure reason or failed rollback,
    /// <c>degraded</c> when degradations were reported, otherwise <c>succeeded</c>.
    /// </summary>
    internal void Complete(
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
