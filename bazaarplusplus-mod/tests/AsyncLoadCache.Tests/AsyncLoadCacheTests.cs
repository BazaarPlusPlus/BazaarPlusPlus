using BazaarPlusPlus.Infrastructure;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class AsyncLoadCacheTests
{
    [Fact]
    public async Task GetOrLoadAsync_ReturnsCachedValueWithoutReloading()
    {
        var loadCount = 0;
        var cache = new AsyncLoadCache<string, object>(_ =>
        {
            loadCount++;
            return Task.FromResult(new AsyncLoadResult<object>(new object(), true));
        });

        var first = await cache.GetOrLoadAsync("hero");
        var second = await cache.GetOrLoadAsync("hero");

        Assert.Same(first, second);
        Assert.Equal(1, loadCount);
    }

    [Fact]
    public async Task GetOrLoadAsync_CoalescesConcurrentLoadsForTheSameKey()
    {
        var loadCount = 0;
        var loaded = new object();
        var pending = new TaskCompletionSource<AsyncLoadResult<object>>();
        var cache = new AsyncLoadCache<string, object>(_ =>
        {
            loadCount++;
            return pending.Task;
        });

        var first = cache.GetOrLoadAsync("hero");
        var second = cache.GetOrLoadAsync("hero");

        Assert.Same(first, second);
        Assert.Equal(1, loadCount);

        pending.SetResult(new AsyncLoadResult<object>(loaded, true));

        Assert.Same(loaded, await first);
        Assert.Same(loaded, await second);
    }

    [Fact]
    public async Task GetOrLoadAsync_NegativelyCachesNullWhenRequested()
    {
        var loadCount = 0;
        var cache = new AsyncLoadCache<string, object>(_ =>
        {
            loadCount++;
            return Task.FromResult(new AsyncLoadResult<object>(null, true));
        });

        Assert.Null(await cache.GetOrLoadAsync("missing"));
        Assert.Null(await cache.GetOrLoadAsync("missing"));

        Assert.Equal(1, loadCount);
        Assert.True(cache.TryGetCached("missing", out var cached));
        Assert.Null(cached);
    }

    [Fact]
    public async Task GetOrLoadAsync_RetriesAfterNonCacheableResult()
    {
        var loadCount = 0;
        var loaded = new object();
        var cache = new AsyncLoadCache<string, object>(_ =>
        {
            loadCount++;
            return Task.FromResult(
                loadCount == 1
                    ? new AsyncLoadResult<object>(null, false)
                    : new AsyncLoadResult<object>(loaded, true)
            );
        });

        Assert.Null(await cache.GetOrLoadAsync("hero"));
        Assert.Same(loaded, await cache.GetOrLoadAsync("hero"));

        Assert.Equal(2, loadCount);
    }

    [Fact]
    public async Task GetOrLoadAsync_ClearsInFlightAndDoesNotCacheWhenLoaderThrows()
    {
        var loadCount = 0;
        var loaded = new object();
        var cache = new AsyncLoadCache<string, object>(_ =>
        {
            loadCount++;
            return loadCount == 1
                ? Task.FromException<AsyncLoadResult<object>>(
                    new InvalidOperationException("load failed")
                )
                : Task.FromResult(new AsyncLoadResult<object>(loaded, true));
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrLoadAsync("hero"));
        Assert.False(cache.TryGetCached("hero", out _));

        Assert.Same(loaded, await cache.GetOrLoadAsync("hero"));
        Assert.Equal(2, loadCount);
    }

    [Fact]
    public void TryGetCached_DoesNotStartLoading()
    {
        var loadCount = 0;
        var cache = new AsyncLoadCache<string, string>(_ =>
        {
            loadCount++;
            return Task.FromResult(new AsyncLoadResult<string>("portrait", true));
        });

        Assert.False(cache.TryGetCached("hero", out var cached));
        Assert.Null(cached);
        Assert.Equal(0, loadCount);
    }
}
