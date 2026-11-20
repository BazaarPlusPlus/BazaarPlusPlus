#nullable enable
using BazaarGameShared.Domain.Cards;

namespace BazaarPlusPlus.GameInterop.StaticCards;

// Serialize loads across replacement managers because both GetCardMap calls can read the
// same SQLite file.
internal sealed class BppStaticCardMapProvider
{
    private readonly object _syncRoot = new();
    private object? _currentSource;
    private Task<Dictionary<Guid, ITCard>?>? _currentLoad;
    private Task _loadTail = Task.CompletedTask;

    public Task<Dictionary<Guid, ITCard>?>? BeginLoad(out object? source)
    {
        source = BppStaticDataAccess.TryGetReadyManagerObject();
        return source == null ? null : BeginLoad(source);
    }

    // A game-data refresh replaces the manager, so cache loads by reference.
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
