#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

public sealed class HistoryTimelineScroll
{
    private string? _selectedBattleId;
    private double? _slotsBeforeLoading;

    // One chip's pitch when the caller names none.
    private const float DefaultPitch = 64;

    // step: one chip's pitch.
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
        // Empty pages also occur between asynchronous loads. Keep the last position and
        // selection so the returning page cannot turn a refresh into another navigation.
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
