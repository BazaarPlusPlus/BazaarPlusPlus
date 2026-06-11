#nullable enable
namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal sealed class ItemBoardPreviewGenerationGuard
{
    private int _generation;

    public int Current => _generation;

    public int Bump()
    {
        return ++_generation;
    }

    public bool IsCurrent(int snapshot)
    {
        return snapshot == _generation;
    }
}
