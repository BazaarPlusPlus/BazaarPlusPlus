#nullable enable
namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeTooltipRefreshTransaction
{
    internal static NativeTooltipRefreshTransactionResult Execute<T>(
        T current,
        Func<T> createReplacement,
        Func<T, bool> write,
        Func<T, bool> apply
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
            var rollback = RollBack(current, write, apply, restoreTooltip: false);
            return new NativeTooltipRefreshTransactionResult(
                rollback.Succeeded
                    ? NativeTooltipRefreshTransactionStatus.WriteFailed
                    : NativeTooltipRefreshTransactionStatus.RollbackFailed,
                rollback.Exception ?? ex
            );
        }

        if (!wroteReplacement)
        {
            var rollback = RollBack(current, write, apply, restoreTooltip: false);
            return new NativeTooltipRefreshTransactionResult(
                rollback.Succeeded
                    ? NativeTooltipRefreshTransactionStatus.WriteFailed
                    : NativeTooltipRefreshTransactionStatus.RollbackFailed,
                rollback.Exception
            );
        }

        try
        {
            if (apply(replacement))
            {
                return new NativeTooltipRefreshTransactionResult(
                    NativeTooltipRefreshTransactionStatus.Refreshed,
                    null
                );
            }
        }
        catch (Exception ex)
        {
            var rollback = RollBack(current, write, apply, restoreTooltip: true);
            return new NativeTooltipRefreshTransactionResult(
                rollback.Succeeded
                    ? NativeTooltipRefreshTransactionStatus.ApplyFailed
                    : NativeTooltipRefreshTransactionStatus.RollbackFailed,
                rollback.Exception ?? ex
            );
        }

        var failedApplyRollback = RollBack(current, write, apply, restoreTooltip: true);
        return new NativeTooltipRefreshTransactionResult(
            failedApplyRollback.Succeeded
                ? NativeTooltipRefreshTransactionStatus.ApplyFailed
                : NativeTooltipRefreshTransactionStatus.RollbackFailed,
            failedApplyRollback.Exception
        );
    }

    private static RollbackResult RollBack<T>(
        T current,
        Func<T, bool> write,
        Func<T, bool> apply,
        bool restoreTooltip
    )
        where T : class
    {
        try
        {
            if (!write(current))
                return new RollbackResult(false, null);
            if (restoreTooltip && !apply(current))
                return new RollbackResult(false, null);
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
    ApplyFailed,
    RollbackFailed,
}

internal readonly record struct NativeTooltipRefreshTransactionResult(
    NativeTooltipRefreshTransactionStatus Status,
    Exception? Exception
);
