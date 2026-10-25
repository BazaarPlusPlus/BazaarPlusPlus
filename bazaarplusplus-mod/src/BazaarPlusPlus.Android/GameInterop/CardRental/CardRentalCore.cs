using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Android.GameInterop.CardRental;

/// <summary>What a lease shows: a card template at a tier, with an optional enchantment.</summary>
internal readonly record struct CardRentalSubject(
    Guid TemplateId,
    CardTier Tier,
    CardEnchantment? Enchantment = null
);

internal enum CardRentalLeaseState
{
    /// <summary>Instantiating or setting up; <see cref="CardRentalLease{TCard}.Card"/> may be null.</summary>
    Pending,

    /// <summary>Set up and shown under the lease's parent.</summary>
    Ready,

    /// <summary>The card could not be rented or set up; it holds no instance.</summary>
    Failed,

    /// <summary>Returned by its owner or released on a scene load; it holds no instance.</summary>
    Released,
}

/// <summary>
/// One rented native card. The owner reads <see cref="State"/> each frame; a lease never changes
/// owner, and a released lease stays released.
/// </summary>
internal sealed class CardRentalLease<TCard>
    where TCard : class
{
    internal CardRentalLease(CardRentalSubject subject, string owner)
    {
        Subject = subject;
        Owner = owner;
    }

    public CardRentalSubject Subject { get; internal set; }

    public string Owner { get; }

    public CardRentalLeaseState State { get; internal set; } = CardRentalLeaseState.Pending;

    public TCard? Card { get; internal set; }

    /// <summary>Bumped each time the lease becomes Ready, so an owner can tell a re-setup apart.</summary>
    public int Revision { get; internal set; }

    public string? FailureReason { get; internal set; }

    internal string? Kind { get; set; }

    /// <summary>Set by a release-all: the instance in flight is destroyed, never pooled.</summary>
    internal bool DestroyOnArrival { get; set; }

    /// <summary>A retarget arrived while a setup was in flight; it runs when that setup ends.</summary>
    internal bool RetargetPending { get; set; }

    internal object? InstantiateOperation { get; set; }

    internal object? SetUpOperation { get; set; }

    internal object? Parent { get; set; }
}

/// <summary>
/// The game-facing half of the rental: everything that touches a native card. The Android host
/// implements it over IL2CPP (<c>AndroidCardRentalBackend</c>); tests implement it over fakes.
/// Every method runs on the main thread; an operation handle is polled until it completes.
/// </summary>
internal interface ICardRentalBackend<TCard, TParent>
    where TCard : class
    where TParent : class
{
    /// <summary>The pool kind the subject's template rents from, or null with a reason.</summary>
    string? ResolveKind(CardRentalSubject subject, out string? failureReason);

    object BeginInstantiate(CardRentalSubject subject, string kind);

    /// <summary>True once the operation finished; <paramref name="card"/> is null when it failed.</summary>
    bool TryCompleteInstantiate(object operation, out TCard? card, out string? failureReason);

    object BeginSetUp(TCard card, CardRentalSubject subject, TParent parent);

    bool TryCompleteSetUp(object operation, TCard card, out string? failureReason);

    /// <summary>Deactivates the card and moves it under the pool root.</summary>
    void Park(TCard card);

    /// <summary>
    /// Moves a card whose setup is still in flight under the pool root, where its setup ends
    /// unseen: the panel that returned it may destroy its own hierarchy before then.
    /// </summary>
    void Detach(TCard card);

    void Destroy(TCard card);

    /// <summary>False once the native object is gone (destroyed by the game or a scene unload).</summary>
    bool IsAlive(TCard card);
}

/// <summary>
/// The native card rental every Android panel shares: per-kind pooling of the game's card UI
/// prefabs, leases polled to readiness, and the counts that prove every instance is accounted for.
/// No IL2CPP call happens here, so AndroidHost.Tests drives it over a fake backend (ADR-0016).
/// <para>
/// Invariant (<see cref="IsBalanced"/>): every instance ever instantiated is destroyed, lost (the
/// game destroyed it), held by a lease, or parked in the pool.
/// </para>
/// </summary>
internal sealed class CardRentalCore<TCard, TParent>
    where TCard : class
    where TParent : class
{
    public const int DefaultMaxPooledPerKind = 30;

    private readonly ICardRentalBackend<TCard, TParent> _backend;
    private readonly int _maxPooledPerKind;
    private readonly Dictionary<string, Queue<TCard>> _pool = new(StringComparer.Ordinal);
    private readonly List<CardRentalLease<TCard>> _active = new();
    private readonly List<CardRentalLease<TCard>> _ticking = new();

    public CardRentalCore(
        ICardRentalBackend<TCard, TParent> backend,
        int maxPooledPerKind = DefaultMaxPooledPerKind
    )
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _maxPooledPerKind = Math.Max(1, maxPooledPerKind);
    }

    public long Rented { get; private set; }

    public long Returned { get; private set; }

    public long Failed { get; private set; }

    public long Instantiated { get; private set; }

    public long PoolHits { get; private set; }

    public long Destroyed { get; private set; }

    public long Lost { get; private set; }

    /// <summary>Leases not yet returned: pending and ready ones.</summary>
    public int ActiveLeases => _active.Count;

    public int Pooled => _pool.Values.Sum(queue => queue.Count);

    /// <summary>Instances held by an active lease, whether set up yet or not.</summary>
    public int Held => _active.Count(lease => lease.Card != null);

    public bool IsBalanced => Instantiated - Destroyed - Lost == Held + Pooled;

    public CardRentalLease<TCard> Rent(CardRentalSubject subject, TParent parent, string owner)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var lease = new CardRentalLease<TCard>(subject, owner) { Parent = parent };
        Rented++;
        _active.Add(lease);
        Start(lease);
        return lease;
    }

    /// <summary>Sets a held lease up again for another subject of the same kind, or re-rents it.</summary>
    public void Retarget(CardRentalLease<TCard> lease, CardRentalSubject subject)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.State is CardRentalLeaseState.Released || !_active.Contains(lease))
            return;
        lease.Subject = subject;
        lease.FailureReason = null;
        lease.State = CardRentalLeaseState.Pending;
        // The instance in flight picks up the new subject when it arrives; a setup in flight
        // finishes first, so the native card never runs two setups at once.
        if (lease.InstantiateOperation != null)
            return;
        if (lease.SetUpOperation != null)
        {
            lease.RetargetPending = true;
            return;
        }
        ApplyRetarget(lease);
    }

    private void ApplyRetarget(CardRentalLease<TCard> lease)
    {
        var kind = SafeResolveKind(lease.Subject, out var failure);
        if (kind == null)
        {
            Fail(lease, failure ?? "kind_unresolved");
            return;
        }
        if (lease.Card != null && string.Equals(lease.Kind, kind, StringComparison.Ordinal))
        {
            BeginSetUp(lease, lease.Card);
            return;
        }
        if (lease.Card != null)
        {
            var card = lease.Card;
            lease.Card = null;
            lease.SetUpOperation = null;
            ParkOrDestroy(lease.Kind!, card);
        }
        lease.Kind = kind;
        Acquire(lease, kind);
    }

    /// <summary>Ends a lease: its instance goes back to the pool once any setup in flight ends.</summary>
    public void Return(CardRentalLease<TCard>? lease)
    {
        if (lease == null || lease.State == CardRentalLeaseState.Released)
            return;
        if (lease.State != CardRentalLeaseState.Failed)
            Returned++;
        lease.State = CardRentalLeaseState.Released;
        if (lease.InstantiateOperation != null || lease.SetUpOperation != null)
        {
            // Tick settles it when the native operation completes; a card already set up in
            // flight leaves the returning panel now, so a view rebuilt meanwhile cannot take it.
            if (lease.Card != null)
                SafeDetach(lease.Card);
            return;
        }
        _active.Remove(lease);
        if (lease.Card != null)
        {
            var card = lease.Card;
            lease.Card = null;
            ParkOrDestroy(lease.Kind!, card);
        }
    }

    /// <summary>
    /// A scene load or the feature switching off: every lease is released, and every instance,
    /// held or pooled, is destroyed; one still instantiating or setting up is destroyed when it
    /// arrives. Returns how many leases were released.
    /// </summary>
    public int ReleaseAll()
    {
        var released = 0;
        foreach (var lease in _active.ToArray())
        {
            if (lease.State != CardRentalLeaseState.Released)
            {
                released++;
                Returned++;
                lease.State = CardRentalLeaseState.Released;
            }
            if (lease.InstantiateOperation != null || lease.SetUpOperation != null)
            {
                lease.DestroyOnArrival = true;
                if (lease.Card != null)
                    SafeDetach(lease.Card);
                continue;
            }
            _active.Remove(lease);
            if (lease.Card != null)
            {
                var card = lease.Card;
                lease.Card = null;
                DestroyInstance(card);
            }
        }
        DestroyPool();
        return released;
    }

    /// <summary>Polls every native operation in flight; call once per frame on the main thread.</summary>
    public void Tick()
    {
        // Advance can remove the lease it settles, so it walks a copy; the copy is reused
        // because this runs every frame while a panel holds cards.
        _ticking.AddRange(_active);
        try
        {
            foreach (var lease in _ticking)
                Advance(lease);
        }
        finally
        {
            _ticking.Clear();
        }
    }

    private void Start(CardRentalLease<TCard> lease)
    {
        var kind = SafeResolveKind(lease.Subject, out var failure);
        if (kind == null)
        {
            Fail(lease, failure ?? "kind_unresolved");
            return;
        }
        lease.Kind = kind;
        Acquire(lease, kind);
    }

    private void Acquire(CardRentalLease<TCard> lease, string kind)
    {
        if (TryTakePooled(kind, out var pooled))
        {
            PoolHits++;
            lease.Card = pooled;
            BeginSetUp(lease, pooled);
            return;
        }
        try
        {
            lease.InstantiateOperation = _backend.BeginInstantiate(lease.Subject, kind);
        }
        catch (Exception exception)
        {
            Fail(lease, "instantiate_threw:" + exception.GetType().Name);
        }
    }

    private void BeginSetUp(CardRentalLease<TCard> lease, TCard card)
    {
        try
        {
            lease.SetUpOperation = _backend.BeginSetUp(card, lease.Subject, (TParent)lease.Parent!);
        }
        catch (Exception exception)
        {
            lease.SetUpOperation = null;
            lease.Card = null;
            DestroyInstance(card);
            Fail(lease, "setup_threw:" + exception.GetType().Name);
        }
    }

    private void Advance(CardRentalLease<TCard> lease)
    {
        if (lease.InstantiateOperation != null)
        {
            TCard? card;
            string? failure;
            bool completed;
            try
            {
                completed = _backend.TryCompleteInstantiate(
                    lease.InstantiateOperation,
                    out card,
                    out failure
                );
            }
            catch (Exception exception)
            {
                completed = true;
                card = null;
                failure = "instantiate_threw:" + exception.GetType().Name;
            }
            if (!completed)
                return;
            lease.InstantiateOperation = null;
            if (card == null)
            {
                if (lease.State == CardRentalLeaseState.Released)
                    _active.Remove(lease);
                else
                    Fail(lease, failure ?? "instantiate_failed");
                return;
            }
            Instantiated++;
            if (lease.State == CardRentalLeaseState.Released)
            {
                _active.Remove(lease);
                if (lease.DestroyOnArrival)
                    DestroyInstance(card);
                else
                    ParkOrDestroy(lease.Kind!, card);
                return;
            }
            lease.Card = card;
            // A retarget while instantiating may have changed the kind.
            var kind = SafeResolveKind(lease.Subject, out _);
            if (kind != null && !string.Equals(kind, lease.Kind, StringComparison.Ordinal))
            {
                lease.Card = null;
                ParkOrDestroy(lease.Kind!, card);
                lease.Kind = kind;
                Acquire(lease, kind);
                return;
            }
            BeginSetUp(lease, card);
            return;
        }

        if (lease.SetUpOperation == null)
            return;
        var held = lease.Card!;
        string? setUpFailure;
        bool setUpCompleted;
        try
        {
            setUpCompleted = _backend.TryCompleteSetUp(
                lease.SetUpOperation,
                held,
                out setUpFailure
            );
        }
        catch (Exception exception)
        {
            setUpCompleted = true;
            setUpFailure = "setup_threw:" + exception.GetType().Name;
        }
        if (!setUpCompleted)
            return; // a retarget that arrived meanwhile stays pending until this setup ends
        lease.SetUpOperation = null;
        var retargetPending = lease.RetargetPending;
        lease.RetargetPending = false;
        if (lease.State == CardRentalLeaseState.Released)
        {
            _active.Remove(lease);
            lease.Card = null;
            if (lease.DestroyOnArrival || setUpFailure != null)
                DestroyInstance(held);
            else
                ParkOrDestroy(lease.Kind!, held);
            return;
        }
        if (setUpFailure != null)
        {
            lease.Card = null;
            DestroyInstance(held);
            Fail(lease, setUpFailure);
            return;
        }
        if (retargetPending)
        {
            ApplyRetarget(lease);
            return;
        }
        lease.State = CardRentalLeaseState.Ready;
        lease.Revision++;
    }

    private void SafeDetach(TCard card)
    {
        try
        {
            if (_backend.IsAlive(card))
                _backend.Detach(card);
        }
        catch (Exception)
        {
            // The setup still settles it; at worst the game destroys it with its parent, and
            // the arrival counts it lost.
        }
    }

    private void Fail(CardRentalLease<TCard> lease, string reason)
    {
        Failed++;
        lease.FailureReason = reason;
        lease.State = CardRentalLeaseState.Failed;
        lease.InstantiateOperation = null;
        lease.SetUpOperation = null;
        _active.Remove(lease);
        if (lease.Card != null)
        {
            var card = lease.Card;
            lease.Card = null;
            DestroyInstance(card);
        }
    }

    private string? SafeResolveKind(CardRentalSubject subject, out string? failure)
    {
        try
        {
            return _backend.ResolveKind(subject, out failure);
        }
        catch (Exception exception)
        {
            failure = "kind_threw:" + exception.GetType().Name;
            return null;
        }
    }

    private bool TryTakePooled(string kind, out TCard card)
    {
        card = null!;
        if (!_pool.TryGetValue(kind, out var queue))
            return false;
        while (queue.Count > 0)
        {
            var candidate = queue.Dequeue();
            if (SafeIsAlive(candidate))
            {
                card = candidate;
                return true;
            }
            Lost++;
        }
        return false;
    }

    private void ParkOrDestroy(string kind, TCard card)
    {
        if (!SafeIsAlive(card))
        {
            Lost++;
            return;
        }
        if (!_pool.TryGetValue(kind, out var queue))
        {
            queue = new Queue<TCard>();
            _pool[kind] = queue;
        }
        if (queue.Count >= _maxPooledPerKind)
        {
            DestroyInstance(card);
            return;
        }
        try
        {
            _backend.Park(card);
        }
        catch
        {
            DestroyInstance(card);
            return;
        }
        queue.Enqueue(card);
    }

    private void DestroyPool()
    {
        foreach (var queue in _pool.Values)
        {
            while (queue.Count > 0)
                DestroyInstance(queue.Dequeue());
        }
        _pool.Clear();
    }

    private void DestroyInstance(TCard card)
    {
        if (!SafeIsAlive(card))
        {
            Lost++;
            return;
        }
        try
        {
            _backend.Destroy(card);
        }
        catch
        {
            // The native object is gone either way once Destroy has been asked for.
        }
        Destroyed++;
    }

    private bool SafeIsAlive(TCard card)
    {
        try
        {
            return _backend.IsAlive(card);
        }
        catch
        {
            return false;
        }
    }
}
