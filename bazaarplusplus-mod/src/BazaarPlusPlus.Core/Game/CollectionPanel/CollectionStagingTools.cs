#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel;

public static class CollectionStagingTools
{
    public static bool IsEnabled(string? rawGameVersion) =>
        rawGameVersion?.IndexOf("-staging", StringComparison.OrdinalIgnoreCase) >= 0;
}
