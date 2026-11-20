#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

public sealed class HistoryTouchNavigation
{
    public bool DetailOpen { get; private set; }
    public bool PlayerBoard { get; set; }
    public float[] ArchiveScrollDp { get; } = new float[3];
    public float? BattleScrollSlots { get; set; }
    public HistoryTimelineScroll BattlePosition { get; } = new();

    public void OpenDetail() => DetailOpen = true;

    public bool Back()
    {
        if (!DetailOpen)
            return false;
        DetailOpen = false;
        return true;
    }
}
