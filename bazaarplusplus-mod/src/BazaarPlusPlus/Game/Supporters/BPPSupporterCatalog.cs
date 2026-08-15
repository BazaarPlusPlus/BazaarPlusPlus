#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Supporters;

internal static class BPPSupporterCatalog
{
    private static readonly object SyncRoot = new();
    private static readonly IReadOnlyList<BPPSupporterEntry> FallbackEntries = new[]
    {
        new BPPSupporterEntry { Name = "Bronze Sponsor A", Tier = 2 },
        new BPPSupporterEntry { Name = "Bronze Sponsor B", Tier = 2 },
        new BPPSupporterEntry { Name = "Silver Sponsor A", Tier = 3 },
        new BPPSupporterEntry { Name = "Silver Sponsor B", Tier = 3 },
        new BPPSupporterEntry { Name = "Gold Sponsor A", Tier = 4 },
    };

    private static IBppConfig? _config;
    private static IReadOnlyList<BPPSupporterEntry>? _currentEntries;
    private static Action? _ensureWarm;

    public static void Install(IBppConfig config)
    {
        lock (SyncRoot)
            _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public static void Reset()
    {
        lock (SyncRoot)
        {
            _config = null;
            _currentEntries = null;
            _ensureWarm = null;
        }
    }

    public static IReadOnlyList<BPPSupporterEntry> GetCurrentEntries()
    {
        Action? ensureWarm;
        lock (SyncRoot)
        {
            if (IsFixedListEnabledUnderLock())
                return BPPSupporterFixedList.Entries;
            ensureWarm = _ensureWarm;
        }

        ensureWarm?.Invoke();
        lock (SyncRoot)
        {
            return BPPSupporterListSourcePolicy.ResolveEntries(
                useFixedList: false,
                _currentEntries,
                FallbackEntries
            );
        }
    }

    internal static void Attach(Action ensureWarm)
    {
        lock (SyncRoot)
            _ensureWarm = ensureWarm ?? throw new ArgumentNullException(nameof(ensureWarm));
    }

    internal static void Publish(IReadOnlyList<BPPSupporterEntry> entries)
    {
        lock (SyncRoot)
            _currentEntries = entries;
    }

    internal static void DetachAndResetProjection()
    {
        lock (SyncRoot)
        {
            _ensureWarm = null;
            _currentEntries = null;
        }
    }

    private static bool IsFixedListEnabledUnderLock() =>
        _config?.UseFixedSupporterListConfig?.Value
        ?? BPPSupporterListSourcePolicy.DefaultUseFixedList;
}
