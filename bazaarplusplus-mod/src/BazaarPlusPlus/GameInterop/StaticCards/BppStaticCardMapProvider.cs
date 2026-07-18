#nullable enable
using BazaarGameShared.Domain.Cards;

namespace BazaarPlusPlus.GameInterop.StaticCards;

/// <summary>
/// Shares the expensive full-card-map load across consumers. A static-data manager gets exactly
/// one load task for its lifetime, and loads for replacement managers are serialized so two
/// <c>GetCardMap()</c> calls never read the same SQLite file concurrently.
/// </summary>
internal sealed class BppStaticCardMapProvider
{
    private readonly object _syncRoot = new();
    private object? _currentSource;
    private Task<Dictionary<Guid, ITCard>?>? _currentLoad;
    private Task _loadTail = Task.CompletedTask;

    /// <summary>
    /// True when the currently ready static-data manager already has a shared load task.
    /// A manager replacement therefore returns false until its own load is kicked.
    /// </summary>
    public bool HasLoadStartedForCurrentSource
    {
        get
        {
            var source = BppStaticDataAccess.TryGetReadyManagerObject();
            if (source == null)
                return false;

            lock (_syncRoot)
                return ReferenceEquals(source, _currentSource) && _currentLoad != null;
        }
    }

    /// <summary>
    /// Starts or returns the shared load for the currently ready static-data manager. Returns
    /// <c>null</c> without blocking when static data is not ready yet.
    /// </summary>
    public Task<Dictionary<Guid, ITCard>?>? BeginLoad(out object? source)
    {
        source = BppStaticDataAccess.TryGetReadyManagerObject();
        return source == null ? null : BeginLoad(source);
    }

    /// <summary>
    /// Starts or returns the shared load for <paramref name="source"/>. Source identity is by
    /// reference because each game-data refresh creates a replacement manager instance.
    /// </summary>
    public Task<Dictionary<Guid, ITCard>?> BeginLoad(object source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        lock (_syncRoot)
        {
            if (ReferenceEquals(source, _currentSource) && _currentLoad != null)
                return _currentLoad;

            var predecessor = _loadTail;
            var load = Task.Run(async () =>
            {
                try
                {
                    await predecessor.ConfigureAwait(false);
                }
                catch
                {
                    // A failed old-manager load must not poison a replacement manager's load.
                }

                return BppStaticDataAccess.LoadCardMap(source);
            });
            _currentSource = source;
            _currentLoad = load;
            _loadTail = load;
            return load;
        }
    }
}
