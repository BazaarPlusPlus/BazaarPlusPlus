#nullable enable
namespace BazaarPlusPlus.Game.Supporters;

public static class BPPSupporterListSourcePolicy
{
    public const bool DefaultUseFixedList = false;

    public static IReadOnlyList<BPPSupporterEntry> ResolveEntries(
        bool useFixedList,
        IReadOnlyList<BPPSupporterEntry>? currentEntries,
        IReadOnlyList<BPPSupporterEntry> fallbackEntries
    )
    {
        if (useFixedList)
            return BPPSupporterFixedList.Entries;

        return currentEntries?.Count > 0 ? currentEntries : fallbackEntries;
    }
}
