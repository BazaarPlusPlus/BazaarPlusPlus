#nullable enable
namespace BazaarPlusPlus.Game.PostCombatImpact.Ui;

internal sealed class NativePreviewFitBatch<T>
{
    private readonly List<T> _requests = [];
    private bool _flushScheduled;
    private int _generation;

    internal bool Enqueue(int generation, T request)
    {
        _requests.Add(request);
        if (_flushScheduled)
            return false;

        _generation = generation;
        _flushScheduled = true;
        return true;
    }

    internal bool TryTake(int generation, out T[] requests)
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

    internal void Reset()
    {
        _requests.Clear();
        _flushScheduled = false;
    }
}
