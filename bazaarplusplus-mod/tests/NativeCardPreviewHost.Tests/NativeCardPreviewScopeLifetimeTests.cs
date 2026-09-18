using BazaarPlusPlus.GameInterop.CardPreview;
using Xunit;

namespace NativeCardPreviewHost.Tests;

public sealed class NativeCardPreviewScopeLifetimeTests
{
    [Fact]
    public async Task Closing_rejects_new_acquisitions_and_waits_for_inflight_work()
    {
        var returned = new List<Resource>();
        var destroyed = new List<Resource>();
        var lifetime = new NativeCardPreviewScopeLifetime<Resource>(returned.Add, destroyed.Add);
        Assert.True(lifetime.TryBeginAcquire(out var acquisition));

        var closing = lifetime.CloseAsync().AsTask();

        Assert.False(closing.IsCompleted);
        Assert.False(lifetime.TryBeginAcquire(out _));

        acquisition.Dispose();
        await closing;
        Assert.Empty(returned);
        Assert.Empty(destroyed);
    }

    [Fact]
    public async Task Close_destroys_each_delivered_session_exactly_once()
    {
        var returned = new List<Resource>();
        var destroyed = new List<Resource>();
        var lifetime = new NativeCardPreviewScopeLifetime<Resource>(returned.Add, destroyed.Add);
        var first = Deliver(lifetime, "first");
        var second = Deliver(lifetime, "second");

        await lifetime.CloseAsync();

        Assert.Empty(returned);
        Assert.Equal(new[] { first, second }, destroyed.OrderBy(resource => resource.Name));
        Assert.False(lifetime.Release(first));
        Assert.False(lifetime.Release(second));
        Assert.Equal(2, destroyed.Count);
    }

    [Fact]
    public async Task Release_before_close_returns_once_and_is_not_destroyed_later()
    {
        var returned = new List<Resource>();
        var destroyed = new List<Resource>();
        var lifetime = new NativeCardPreviewScopeLifetime<Resource>(returned.Add, destroyed.Add);
        var resource = Deliver(lifetime, "returned");

        Assert.True(lifetime.Release(resource));
        Assert.False(lifetime.Release(resource));
        await lifetime.CloseAsync();

        Assert.Equal(new[] { resource }, returned);
        Assert.Empty(destroyed);
    }

    [Fact]
    public async Task Release_after_closing_starts_destroys_instead_of_returning()
    {
        var returned = new List<Resource>();
        var destroyed = new List<Resource>();
        var lifetime = new NativeCardPreviewScopeLifetime<Resource>(returned.Add, destroyed.Add);
        var active = Deliver(lifetime, "active");
        Assert.True(lifetime.TryBeginAcquire(out var blocker));
        var closing = lifetime.CloseAsync().AsTask();

        Assert.True(lifetime.Release(active));
        Assert.False(lifetime.Release(active));
        blocker.Dispose();
        await closing;

        Assert.Empty(returned);
        Assert.Equal(new[] { active }, destroyed);
    }

    [Fact]
    public async Task Acquisition_cannot_deliver_after_closing_starts()
    {
        var returned = new List<Resource>();
        var destroyed = new List<Resource>();
        var lifetime = new NativeCardPreviewScopeLifetime<Resource>(returned.Add, destroyed.Add);
        Assert.True(lifetime.TryBeginAcquire(out var acquisition));
        var closing = lifetime.CloseAsync().AsTask();

        Assert.False(acquisition.TryDeliver(new Resource("late")));
        acquisition.Dispose();
        await closing;

        Assert.Empty(returned);
        Assert.Empty(destroyed);
    }

    [Fact]
    public async Task Close_attempts_every_active_destroy_when_one_cleanup_fails()
    {
        var destroyed = new List<Resource>();
        var lifetime = new NativeCardPreviewScopeLifetime<Resource>(
            _ => throw new InvalidOperationException("not returned"),
            resource =>
            {
                destroyed.Add(resource);
                if (resource.Name == "first")
                    throw new InvalidOperationException("simulated owner cleanup failure");
            }
        );
        Deliver(lifetime, "first");
        Deliver(lifetime, "second");

        await Assert.ThrowsAsync<AggregateException>(() => lifetime.CloseAsync().AsTask());

        Assert.Equal(
            new[] { "first", "second" },
            destroyed.Select(resource => resource.Name).OrderBy(name => name)
        );
    }

    [Fact]
    public async Task Native_destroy_forgets_active_resource_without_returning_or_destroying_again()
    {
        var returned = new List<Resource>();
        var destroyed = new List<Resource>();
        var lifetime = new NativeCardPreviewScopeLifetime<Resource>(returned.Add, destroyed.Add);
        var resource = Deliver(lifetime, "native-destroyed");

        Assert.True(lifetime.ForgetDestroyed(resource));
        Assert.False(lifetime.ForgetDestroyed(resource));
        Assert.False(lifetime.Release(resource));
        await lifetime.CloseAsync();

        Assert.Empty(returned);
        Assert.Empty(destroyed);
    }

    private static Resource Deliver(NativeCardPreviewScopeLifetime<Resource> lifetime, string name)
    {
        Assert.True(lifetime.TryBeginAcquire(out var acquisition));
        var resource = new Resource(name);
        Assert.True(acquisition.TryDeliver(resource));
        acquisition.Dispose();
        return resource;
    }

    private sealed record Resource(string Name);
}
