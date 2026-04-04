namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class PreviewRenderGenerationGate
{
    private int _generation;
    private bool _hidden;
    private bool _disposed;

    public int BeginRender(bool visible)
    {
        _hidden = !visible;
        _generation++;
        return _generation;
    }

    public int InvalidateForHide()
    {
        _hidden = true;
        _generation++;
        return _generation;
    }

    public void MarkVisible()
    {
        _hidden = false;
    }

    public void MarkDisposed()
    {
        _disposed = true;
        _hidden = true;
        _generation++;
    }

    public bool ShouldCancel(int generation)
    {
        return _disposed || _hidden || generation != _generation;
    }
}
