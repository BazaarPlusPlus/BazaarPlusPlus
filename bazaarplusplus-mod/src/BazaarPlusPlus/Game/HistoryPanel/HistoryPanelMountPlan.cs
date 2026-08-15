#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel;

internal enum HistoryPanelMountMode
{
    DoNotMount,
    MountLocalOnly,
    MountWithOnline,
}

internal static class HistoryPanelMountPlan
{
    internal static HistoryPanelMountMode Resolve(
        bool hasReplayRuntime,
        bool hasOverlayHost,
        bool hasModApiSession
    )
    {
        if (!hasReplayRuntime || !hasOverlayHost)
            return HistoryPanelMountMode.DoNotMount;
        return hasModApiSession
            ? HistoryPanelMountMode.MountWithOnline
            : HistoryPanelMountMode.MountLocalOnly;
    }
}
