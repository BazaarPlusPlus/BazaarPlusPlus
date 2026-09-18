using BazaarPlusPlus.GameInterop.CardPreview;
using Xunit;

namespace NativeCardPreviewHost.Tests;

public sealed class NativeCardPreviewScopeDisposalTests
{
    [Fact]
    public async Task Concurrent_callers_wait_for_the_same_complete_cleanup()
    {
        var cleanupStarted = 0;
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposal = new NativeCardPreviewScopeDisposal(async () =>
        {
            Interlocked.Increment(ref cleanupStarted);
            await cleanup.Task;
        });

        var first = disposal.DisposeAsync().AsTask();
        var second = disposal.DisposeAsync().AsTask();

        Assert.Equal(1, cleanupStarted);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        cleanup.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, cleanupStarted);
    }

    [Fact]
    public async Task Reentrant_dispose_observes_the_seeded_completion_instead_of_restarting()
    {
        var cleanupStarted = 0;
        var releaseCleanup = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        NativeCardPreviewScopeDisposal? disposal = null;
        Task? reentrant = null;
        disposal = new NativeCardPreviewScopeDisposal(async () =>
        {
            Interlocked.Increment(ref cleanupStarted);
            reentrant = disposal!.DisposeAsync().AsTask();
            await releaseCleanup.Task;
        });

        var first = disposal.DisposeAsync().AsTask();

        Assert.Equal(1, cleanupStarted);
        Assert.NotNull(reentrant);
        Assert.False(reentrant!.IsCompleted);

        releaseCleanup.SetResult();
        await Task.WhenAll(first, reentrant);
        Assert.Equal(1, cleanupStarted);
    }
}
