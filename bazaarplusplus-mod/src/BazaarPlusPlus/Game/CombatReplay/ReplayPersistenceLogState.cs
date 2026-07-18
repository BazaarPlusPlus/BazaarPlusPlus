#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class ReplayPersistenceCompletionGate
{
    private int _completed;

    internal bool TryComplete() => Interlocked.Exchange(ref _completed, 1) == 0;
}

internal readonly record struct ReplayOrphanCleanupResult(
    ReplayPersistenceReasonCode ReasonCode,
    int FailedCount,
    Exception? Exception
);

internal sealed class ReplayOrphanCleanupAccumulator
{
    private readonly object _gate = new();
    private int _failedCount;
    private ReplayPersistenceReasonCode _reasonCode =
        ReplayPersistenceReasonCode.OrphanDeleteFailed;
    private Exception? _scanFailure;

    internal void ReportDeleteFailure(Exception exception)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        lock (_gate)
        {
            _failedCount++;
        }
    }

    internal void ReportScanFailure(Exception exception)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        lock (_gate)
        {
            _reasonCode = ReplayPersistenceReasonCode.OrphanScanFailed;
            _scanFailure = exception;
        }
    }

    internal bool TryBuildResult(out ReplayOrphanCleanupResult result)
    {
        lock (_gate)
        {
            if (_failedCount == 0 && _scanFailure == null)
            {
                result = default;
                return false;
            }

            result = new ReplayOrphanCleanupResult(_reasonCode, _failedCount, _scanFailure);
            return true;
        }
    }
}

internal static class ReplayPersistenceLogWriter
{
    internal static void EmitOrphanCleanupDegraded(ReplayOrphanCleanupResult result)
    {
        var fields = new[]
        {
            CombatReplayLogEvents.OrphanCleanupReasonCode.Bind(result.ReasonCode),
            CombatReplayLogEvents.OrphanCleanupFailedCount.Bind(result.FailedCount),
        };
        if (result.Exception == null)
        {
            BppLog.WarnEvent(CombatReplayLogEvents.OrphanCleanupDegraded, fields);
            return;
        }

        BppLog.WarnEvent(CombatReplayLogEvents.OrphanCleanupDegraded, result.Exception, fields);
    }
}
