#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel;

// Generation and input-release decisions stay independent of Unity and async completion order.
internal sealed class CollectionInspectionState
{
    private int _revision;
    private int _releasedFrame = -1;
    public bool IsOpen { get; private set; }

    public int Open()
    {
        IsOpen = true;
        return ++_revision;
    }

    public int Select() => ++_revision;

    public bool CanPublish(int revision) => IsOpen && revision == _revision;

    public void Close(int frame)
    {
        IsOpen = false;
        ++_revision;
        _releasedFrame = frame;
    }

    public bool BlocksInput(int frame) => IsOpen || frame <= _releasedFrame;
}
