#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel;

internal static class CollectionStagingTools
{
    internal static bool IsEnabled(string? rawGameVersion) =>
        rawGameVersion?.IndexOf("-staging", StringComparison.OrdinalIgnoreCase) >= 0;
}
