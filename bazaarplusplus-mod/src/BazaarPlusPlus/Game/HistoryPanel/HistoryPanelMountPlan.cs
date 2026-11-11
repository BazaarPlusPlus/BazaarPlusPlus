#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelMountPlan
{
    internal static bool ShouldMount(bool hasReplayRuntime, bool hasOverlayHost) =>
        hasReplayRuntime && hasOverlayHost;
}
