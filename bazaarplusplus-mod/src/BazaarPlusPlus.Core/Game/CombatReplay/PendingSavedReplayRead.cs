#nullable enable
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.CombatReplay;

// Contains only managed storage reads. The worker releases its reference even when the host
// stops pumping continuations; the owner's reference transfers to playback without a gate gap.
internal sealed class PendingSavedReplayRead
{
    private IDisposable? _owner;
    private readonly IDisposable _worker;

    public PendingSavedReplayRead(IDisposable lease)
    {
        var shared = new SharedLease(lease);
        _owner = new Reference(shared);
        _worker = new Reference(shared);
    }

    public bool Interrupted { get; set; }

    public IDisposable TakeOwnerLease() =>
        Interlocked.Exchange(ref _owner, null)
        ?? throw new InvalidOperationException("The read no longer owns its playback lease.");

    public void ReleaseOwner() => Interlocked.Exchange(ref _owner, null)?.Dispose();

    public Task<ReadResult> LoadAsync(SavedReplayCatalog catalog, string battleId)
    {
        try
        {
            // Do not pass a cancellation token: the worker must run its lease-release finally.
            return Task.Run(() =>
            {
                try
                {
                    catalog.TryLoad(battleId, out var manifest, out var payload);
                    return new ReadResult(manifest, payload, null);
                }
                catch (Exception exception)
                {
                    // A stopped host can discard the continuation. Keep faults observed.
                    return new ReadResult(null, null, exception);
                }
                finally
                {
                    _worker.Dispose();
                }
            });
        }
        catch
        {
            _worker.Dispose();
            throw;
        }
    }

    private sealed class SharedLease(IDisposable lease)
    {
        private int _references = 2;

        public void Release()
        {
            if (Interlocked.Decrement(ref _references) == 0)
                lease.Dispose();
        }
    }

    private sealed class Reference(SharedLease shared) : IDisposable
    {
        private SharedLease? _shared = shared;

        public void Dispose() => Interlocked.Exchange(ref _shared, null)?.Release();
    }

    internal sealed record ReadResult(
        PvpBattleManifest? Manifest,
        PvpReplayPayload? Payload,
        Exception? Exception
    );
}
