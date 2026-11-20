using System.Globalization;
using BazaarPlusPlus.Core.Cards.ItemBoards;

namespace BazaarPlusPlus.Core.Cards.CardRental;

// Saved attribute keys are game member names. The saved size may be smaller than the
// template size because of Tiny, and board cards use the native ammo bar.
public readonly record struct CardRentalSubject(
    Guid TemplateId,
    CardTier Tier,
    CardEnchantment? Enchantment = null,
    IReadOnlyDictionary<string, int>? Attributes = null,
    CardSize? Size = null,
    bool OnBoard = false
)
{
    public static CardRentalSubject Of(BppItemBoardCard card) =>
        new(
            card.TemplateId,
            card.Tier,
            card.EnchantmentType,
            card.Attributes,
            card.Size,
            OnBoard: true
        );

    public CardSize ShownSize(CardSize templateSize) => Size ?? templateSize;

    public bool IsTinyOn(CardSize templateSize) => Size is { } size && size < templateSize;

    // Panels use this key to re-rent a card when only its attribute values change.
    public static string AttributesKey(IReadOnlyDictionary<string, int>? attributes) =>
        attributes is not { Count: > 0 }
            ? string.Empty
            : string.Join(
                ",",
                attributes
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair =>
                        pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture)
                    )
            );
}

public enum CardRentalLeaseState
{
    Pending,

    Ready,

    Failed,

    Released,
}

// A lease stays with its original owner and cannot be revived after release.
public sealed class CardRentalLease<TCard>
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

    // Owners use the revision to detect another setup on the same lease.
    public int Revision { get; internal set; }

    public string? FailureReason { get; internal set; }

    internal string? Kind { get; set; }

    // Release-all must destroy any instance that arrives after the scene changes.
    internal bool DestroyOnArrival { get; set; }

    internal bool RetargetPending { get; set; }

    internal object? InstantiateOperation { get; set; }

    internal object? SetUpOperation { get; set; }

    internal object? Parent { get; set; }
}

// The native backend is main-thread-only. Poll asynchronous operations on that same thread.
public interface ICardRentalBackend<TCard, TParent>
    where TCard : class
    where TParent : class
{
    string? ResolveKind(CardRentalSubject subject, out string? failureReason);

    object BeginInstantiate(CardRentalSubject subject, string kind);

    bool TryCompleteInstantiate(object operation, out TCard? card, out string? failureReason);

    object BeginSetUp(TCard card, CardRentalSubject subject, TParent parent);

    bool TryCompleteSetUp(object operation, TCard card, out string? failureReason);

    void Park(TCard card);

    /// <summary>
    /// Moves a card whose setup is still in flight under the pool root, where its setup ends
    /// unseen: the panel that returned it may destroy its own hierarchy before then.
    /// </summary>
    void Detach(TCard card);

    void Destroy(TCard card);

    // Scene unload can destroy a native object while its managed wrapper still exists.
    bool IsAlive(TCard card);
}

// Call Tick once per frame on the main thread.
// Account for every instantiated card as destroyed, lost, leased or pooled.
public sealed class CardRentalCore<TCard, TParent>
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

    public int ActiveLeases => _active.Count;

    public int Pooled => _pool.Values.Sum(queue => queue.Count);

    public int Held => _active.Count(lease => lease.Card != null);

    public bool IsBalanced => Instantiated - Destroyed - Lost == Held + Pooled;

    public CardRentalLease<TCard> Rent(CardRentalSubject subject, TParent parent, string owner)
    {
        if (parent == null)
            throw new ArgumentNullException(nameof(parent));
        var lease = new CardRentalLease<TCard>(subject, owner) { Parent = parent };
        Rented++;
        _active.Add(lease);
        Start(lease);
        return lease;
    }

    public void Retarget(CardRentalLease<TCard> lease, CardRentalSubject subject)
    {
        if (lease == null)
            throw new ArgumentNullException(nameof(lease));
        if (lease.State is CardRentalLeaseState.Released || !_active.Contains(lease))
            return;
        lease.Subject = subject;
        lease.FailureReason = null;
        lease.State = CardRentalLeaseState.Pending;
        // The native card cannot run two setups at once.
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

    public void Return(CardRentalLease<TCard>? lease)
    {
        if (lease == null || lease.State == CardRentalLeaseState.Released)
            return;
        if (lease.State != CardRentalLeaseState.Failed)
            Returned++;
        lease.State = CardRentalLeaseState.Released;
        if (lease.InstantiateOperation != null || lease.SetUpOperation != null)
        {
            // Detach before the native setup settles so a rebuilt panel cannot destroy the card.
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
            // Count a requested destroy as settled even if the backend reports an error.
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
