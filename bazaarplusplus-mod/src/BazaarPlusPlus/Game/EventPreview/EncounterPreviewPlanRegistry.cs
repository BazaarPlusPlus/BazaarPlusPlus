#nullable enable
namespace BazaarPlusPlus.Game.EventPreview;

internal sealed class EncounterPreviewPlanRegistry
{
    private readonly object _gate = new();
    private object? _generationSource;
    private long _generation;
    private PublishedSnapshot? _published;

    public long BeginGeneration(object source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        lock (_gate)
        {
            _generation = unchecked(_generation + 1);
            _generationSource = source;
            Volatile.Write(ref _published, null);
            return _generation;
        }
    }

    public bool IsCurrent(object source, long generation)
    {
        lock (_gate)
            return IsCurrentUnderLock(source, generation);
    }

    public bool TryPublish(object source, long generation, EncounterPreviewSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        lock (_gate)
        {
            if (!IsCurrentUnderLock(source, generation))
                return false;

            Volatile.Write(ref _published, new PublishedSnapshot(source, snapshot));
            return true;
        }
    }

    public bool TryCommitAndPublish(
        object source,
        long generation,
        EncounterPreviewSnapshot snapshot,
        Action commit
    )
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        if (commit == null)
            throw new ArgumentNullException(nameof(commit));

        lock (_gate)
        {
            if (!IsCurrentUnderLock(source, generation))
                return false;

            commit();
            Volatile.Write(ref _published, new PublishedSnapshot(source, snapshot));
            return true;
        }
    }

    public bool TryGet(object source, out EncounterPreviewSnapshot snapshot)
    {
        var published = Volatile.Read(ref _published);
        if (published != null && ReferenceEquals(source, published.Source))
        {
            snapshot = published.Snapshot;
            return true;
        }

        snapshot = null!;
        return false;
    }

    public bool TryRunIfCurrent(object source, long generation, Action action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        lock (_gate)
        {
            if (!IsCurrentUnderLock(source, generation))
                return false;
            action();
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _generation = unchecked(_generation + 1);
            _generationSource = null;
            Volatile.Write(ref _published, null);
        }
    }

    private bool IsCurrentUnderLock(object source, long generation) =>
        generation == _generation && ReferenceEquals(source, _generationSource);

    private sealed class PublishedSnapshot
    {
        public PublishedSnapshot(object source, EncounterPreviewSnapshot snapshot)
        {
            Source = source;
            Snapshot = snapshot;
        }

        public object Source { get; }

        public EncounterPreviewSnapshot Snapshot { get; }
    }
}
