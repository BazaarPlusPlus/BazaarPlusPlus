#nullable enable
namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeTooltipRefreshTransaction
{
    internal static NativeTooltipRefreshTransactionResult Execute<T>(
        T current,
        Func<T> createReplacement,
        Func<T, bool> write,
        Action beforeRehover,
        Func<bool> rehover
    )
        where T : class
    {
        T replacement;
        try
        {
            replacement = createReplacement();
        }
        catch (Exception ex)
        {
            return new NativeTooltipRefreshTransactionResult(
                NativeTooltipRefreshTransactionStatus.CreateFailed,
                ex
            );
        }

        if (ReferenceEquals(replacement, current))
            return new NativeTooltipRefreshTransactionResult(
                NativeTooltipRefreshTransactionStatus.NoChange,
                null
            );

        bool wroteReplacement;
        try
        {
            wroteReplacement = write(replacement);
        }
        catch (Exception ex)
        {
            var rollback = RollBack(current, write, beforeRehover, rehover, restoreTooltip: false);
            return new NativeTooltipRefreshTransactionResult(
                rollback.Succeeded
                    ? NativeTooltipRefreshTransactionStatus.WriteFailed
                    : NativeTooltipRefreshTransactionStatus.RollbackFailed,
                rollback.Exception ?? ex
            );
        }

        if (!wroteReplacement)
        {
            var rollback = RollBack(current, write, beforeRehover, rehover, restoreTooltip: false);
            return new NativeTooltipRefreshTransactionResult(
                rollback.Succeeded
                    ? NativeTooltipRefreshTransactionStatus.WriteFailed
                    : NativeTooltipRefreshTransactionStatus.RollbackFailed,
                rollback.Exception
            );
        }

        try
        {
            beforeRehover();
            if (rehover())
            {
                return new NativeTooltipRefreshTransactionResult(
                    NativeTooltipRefreshTransactionStatus.Refreshed,
                    null
                );
            }
        }
        catch (Exception ex)
        {
            var rollback = RollBack(current, write, beforeRehover, rehover, restoreTooltip: true);
            return new NativeTooltipRefreshTransactionResult(
                rollback.Succeeded
                    ? NativeTooltipRefreshTransactionStatus.RehoverFailed
                    : NativeTooltipRefreshTransactionStatus.RollbackFailed,
                rollback.Exception ?? ex
            );
        }

        var failedRehoverRollback = RollBack(
            current,
            write,
            beforeRehover,
            rehover,
            restoreTooltip: true
        );
        return new NativeTooltipRefreshTransactionResult(
            failedRehoverRollback.Succeeded
                ? NativeTooltipRefreshTransactionStatus.RehoverFailed
                : NativeTooltipRefreshTransactionStatus.RollbackFailed,
            failedRehoverRollback.Exception
        );
    }

    private static RollbackResult RollBack<T>(
        T current,
        Func<T, bool> write,
        Action beforeRehover,
        Func<bool> rehover,
        bool restoreTooltip
    )
        where T : class
    {
        try
        {
            if (!write(current))
                return new RollbackResult(false, null);
            if (restoreTooltip)
            {
                beforeRehover();
                if (!rehover())
                    return new RollbackResult(false, null);
            }
            return new RollbackResult(true, null);
        }
        catch (Exception ex)
        {
            return new RollbackResult(false, ex);
        }
    }

    private readonly record struct RollbackResult(bool Succeeded, Exception? Exception);
}

internal enum NativeTooltipRefreshTransactionStatus
{
    Refreshed,
    NoChange,
    CreateFailed,
    WriteFailed,
    RehoverFailed,
    RollbackFailed,
}

internal readonly record struct NativeTooltipRefreshTransactionResult(
    NativeTooltipRefreshTransactionStatus Status,
    Exception? Exception
);
