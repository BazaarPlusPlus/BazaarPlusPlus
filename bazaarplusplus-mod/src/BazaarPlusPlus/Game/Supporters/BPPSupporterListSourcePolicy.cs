#nullable enable
namespace BazaarPlusPlus.Game.Supporters;

internal static class BPPSupporterListSourcePolicy
{
    internal const bool DefaultUseFixedList = false;

    internal static IReadOnlyList<BPPSupporterEntry> ResolveEntries(
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
