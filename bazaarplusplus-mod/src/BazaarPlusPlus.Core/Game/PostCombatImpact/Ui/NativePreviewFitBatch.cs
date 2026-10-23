#nullable enable
namespace BazaarPlusPlus.Game.PostCombatImpact.Ui;

public sealed class NativePreviewFitBatch<T>
{
    private readonly List<T> _requests = [];
    private bool _flushScheduled;
    private int _generation;

    public bool Enqueue(int generation, T request)
    {
        _requests.Add(request);
        if (_flushScheduled)
            return false;

        _generation = generation;
        _flushScheduled = true;
        return true;
    }

    public bool TryTake(int generation, out T[] requests)
    {
        if (!_flushScheduled || generation != _generation)
        {
            requests = [];
            return false;
        }

        requests = _requests.ToArray();
        _requests.Clear();
        _flushScheduled = false;
        return true;
    }

    public void Reset()
    {
        _requests.Clear();
        _flushScheduled = false;
    }
}
