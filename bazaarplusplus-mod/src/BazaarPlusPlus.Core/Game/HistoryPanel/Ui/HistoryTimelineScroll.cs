#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

public sealed class HistoryTimelineScroll
{
    private string? _selectedBattleId;
    private double? _slotsBeforeLoading;

    private const float DefaultPitch = 64;

    public float Bind(
        string? selectedBattleId,
        int selectedSlot,
        int rowCount,
        float offset,
        float viewportHeight,
        float? step = null
    )
    {
        var pitch = step ?? DefaultPitch;
        // An in-flight page can be temporarily empty. Keep the previous position and selection so refresh does not become navigation.
        if (rowCount == 0 || viewportHeight <= 0)
        {
            _slotsBeforeLoading ??= (double)offset / pitch;
            return 0;
        }

        offset = _slotsBeforeLoading is { } slots ? (float)(slots * pitch) : offset;
        _slotsBeforeLoading = null;
        var max = System.Math.Max(0, rowCount * pitch - viewportHeight);
        offset = System.Math.Clamp(offset, 0, max);

        if (selectedBattleId != _selectedBattleId && selectedSlot >= 0 && selectedSlot < rowCount)
        {
            var top = selectedSlot * pitch;
            var bottom = top + pitch - 5;
            // Keep partly visible chips in place so they remain clickable under the pointer.
            if (bottom <= offset)
                offset = top;
            else if (top >= offset + viewportHeight)
                offset = bottom - viewportHeight;
        }

        _selectedBattleId = selectedBattleId;
        return System.Math.Clamp(offset, 0, max);
    }
}
