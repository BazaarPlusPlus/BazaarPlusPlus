using BazaarPlusPlus.Game.CombatReplay;
using Xunit;

namespace BazaarPlusPlus.Tests.SavedReplays;

public sealed class SavedReplayOfferCacheTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Repeated_view_and_click_probes_share_one_read_until_five_seconds(bool available)
    {
        var reads = 0;
        var cache = new SavedReplayOfferCache(_ =>
        {
            reads++;
            return available;
        });
        Assert.Equal(available, cache.CanOffer("battle", 10f));
        available = !available;
        Assert.Equal(!available, cache.CanOffer("battle", 10f));
        Assert.Equal(!available, cache.CanOffer("battle", 12f));
        Assert.Equal(!available, cache.CanOffer("battle", 14.999f));
        Assert.Equal(1, reads);
        Assert.Equal(available, cache.CanOffer("battle", 15f));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void Taking_an_offer_invalidates_only_that_battle()
    {
        var reads = new List<string>();
        var cache = new SavedReplayOfferCache(id =>
        {
            reads.Add(id);
            return true;
        });
        cache.CanOffer("a", 0f);
        cache.CanOffer("b", 0f);
        cache.Forget("a");
        cache.CanOffer("a", 1f);
        cache.CanOffer("b", 1f);
        Assert.Equal(["a", "b", "a"], reads);
    }

    [Fact]
    public void An_expired_success_does_not_mask_a_read_failure_or_delay_its_retry()
    {
        var reads = 0;
        var cache = new SavedReplayOfferCache(_ =>
        {
            reads++;
            if (reads == 2)
                throw new IOException("catalog unavailable");
            return true;
        });
        Assert.True(cache.CanOffer("battle", 0f));
        Assert.Throws<IOException>(() => cache.CanOffer("battle", 5f));
        Assert.True(cache.CanOffer("battle", 5.1f));
        Assert.Equal(3, reads);
    }

    [Fact]
    public void A_first_failure_is_never_cached_as_missing()
    {
        var reads = 0;
        var cache = new SavedReplayOfferCache(_ =>
        {
            if (++reads == 1)
                throw new IOException("catalog unavailable");
            return true;
        });
        Assert.Throws<IOException>(() => cache.CanOffer("battle", 0f));
        Assert.True(cache.CanOffer("battle", 0f));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void A_reset_clock_cannot_extend_a_stale_offer()
    {
        var available = true;
        var cache = new SavedReplayOfferCache(_ => available);
        Assert.True(cache.CanOffer("battle", 100f));
        available = false;
        Assert.False(cache.CanOffer("battle", 0f));
    }

    [Fact]
    public void Browsing_many_battles_evicts_old_offers()
    {
        var reads = 0;
        var cache = new SavedReplayOfferCache(_ =>
        {
            reads++;
            return true;
        });
        cache.CanOffer("first", 0f);
        for (var i = 0; i < 257; i++)
            cache.CanOffer(i.ToString(), 0f);
        var before = reads;
        cache.CanOffer("first", 0f);
        Assert.Equal(before + 1, reads);
    }
}
