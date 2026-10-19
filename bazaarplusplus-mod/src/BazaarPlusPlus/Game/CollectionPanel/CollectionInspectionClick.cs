#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionInspectionClick
{
    private Guid? _card;
    private float _x,
        _y,
        _scroll;

    public bool Sample(bool pressed, bool released, Guid? card, float x, float y, float scroll)
    {
        if (pressed)
        {
            _card = card;
            _x = x;
            _y = y;
            _scroll = scroll;
        }
        if (
            _card != card
            || (x - _x) * (x - _x) + (y - _y) * (y - _y) > 36f
            || Math.Abs(scroll - _scroll) > .5f
        )
            Reset();
        if (!released)
            return false;
        var open = _card.HasValue;
        Reset();
        return open;
    }

    public void Reset() => _card = null;
}
