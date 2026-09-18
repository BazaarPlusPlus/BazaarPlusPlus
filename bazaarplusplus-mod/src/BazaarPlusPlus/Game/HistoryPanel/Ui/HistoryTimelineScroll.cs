#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed class HistoryTimelineScroll
{
    private string? _selectedBattleId;
    private float? _offsetBeforeLoading;

    internal float Bind(
        string? selectedBattleId,
        int selectedSlot,
        int rowCount,
        float offset,
        float viewportHeight
    )
    {
        // Empty pages also occur between asynchronous loads. Keep the last position and
        // selection so the returning page cannot turn a refresh into another navigation.
        if (rowCount == 0 || viewportHeight <= 0)
        {
            _offsetBeforeLoading ??= offset;
            return 0;
        }

        offset = _offsetBeforeLoading ?? offset;
        _offsetBeforeLoading = null;
        var step = HistoryPanelLayout.DayChipHeight;
        var max = System.Math.Max(0, rowCount * step - viewportHeight);
        offset = System.Math.Clamp(offset, 0, max);

        if (selectedBattleId != _selectedBattleId && selectedSlot >= 0 && selectedSlot < rowCount)
        {
            var top = selectedSlot * step;
            var bottom = top + step - 5;
            // A clickable chip, even a partly clipped one, stays under the pointer.
            // Reveal a new selection only when it is entirely outside the viewport.
            if (bottom <= offset)
                offset = top;
            else if (top >= offset + viewportHeight)
                offset = bottom - viewportHeight;
        }

        _selectedBattleId = selectedBattleId;
        return System.Math.Clamp(offset, 0, max);
    }
}
