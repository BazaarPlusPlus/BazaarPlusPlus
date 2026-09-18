using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class NativePreviewFitBatchTests
{
    [Fact]
    public void Same_generation_acquisitions_schedule_one_ordered_flush()
    {
        var batch = new NativePreviewFitBatch<string>();

        Assert.True(batch.Enqueue(7, "first"));
        Assert.False(batch.Enqueue(7, "second"));

        Assert.True(batch.TryTake(7, out var requests));
        Assert.Equal(["first", "second"], requests);
    }

    [Fact]
    public void Stale_flush_cannot_take_replacement_generation_requests()
    {
        var batch = new NativePreviewFitBatch<string>();
        batch.Enqueue(7, "stale");

        batch.Reset();
        Assert.True(batch.Enqueue(8, "replacement"));

        Assert.False(batch.TryTake(7, out var staleRequests));
        Assert.Empty(staleRequests);
        Assert.True(batch.TryTake(8, out var replacementRequests));
        Assert.Equal(["replacement"], replacementRequests);
    }
}
