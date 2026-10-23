using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Core.Cards;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The native card rental's ledger (#396), driven over a fake backend: every instance the game
/// instantiated is destroyed, lost, held or pooled, whatever order rents, returns, retargets,
/// release-alls and native completions arrive in.
/// </summary>
public sealed class CardRentalCoreTests
{
    private static readonly Guid Small = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Medium = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Missing = Guid.Parse("00000000-0000-0000-0000-0000000000ff");

    [Fact]
    public void A_rent_becomes_ready_after_instantiate_and_setup_and_its_return_pools_the_card()
    {
        var backend = new FakeBackend();
        var rental = new CardRentalCore<FakeCard, string>(backend);

        var lease = rental.Rent(new(Small, CardTier.Bronze), "grid", "test");
        Assert.Equal(CardRentalLeaseState.Pending, lease.State);
        backend.CompleteAll();
        rental.Tick();
        Assert.Equal(CardRentalLeaseState.Pending, lease.State);
        backend.CompleteAll();
        rental.Tick();

        Assert.Equal(CardRentalLeaseState.Ready, lease.State);
        Assert.Equal("grid", lease.Card!.Parent);
        Assert.Equal(1, lease.Revision);
        rental.Return(lease);
        Assert.Equal(CardRentalLeaseState.Released, lease.State);
        Assert.Null(lease.Card);
        Assert.Equal(1, rental.Pooled);
        Assert.Equal(0, rental.ActiveLeases);
        Assert.True(rental.IsBalanced);
    }

    [Fact]
    public void A_second_rent_of_the_same_kind_reuses_the_pooled_card()
    {
        var backend = new FakeBackend();
        var rental = new CardRentalCore<FakeCard, string>(backend);
        var first = RentReady(rental, backend, new(Small, CardTier.Bronze));
        var card = first.Card;
        rental.Return(first);

        var second = rental.Rent(new(Small, CardTier.Gold), "grid", "test");
        backend.CompleteAll();
        rental.Tick();

        Assert.Equal(CardRentalLeaseState.Ready, second.State);
        Assert.Same(card, second.Card);
        Assert.Equal(CardTier.Gold, second.Card!.SetUpTier);
        Assert.Equal(1, rental.Instantiated);
        Assert.Equal(1, rental.PoolHits);
        Assert.True(rental.IsBalanced);
    }

    [Fact]
    public void A_full_pool_destroys_what_it_cannot_hold()
    {
        var backend = new FakeBackend();
        var rental = new CardRentalCore<FakeCard, string>(backend, maxPooledPerKind: 1);
        var leases = Enumerable
            .Range(0, 3)
            .Select(_ => rental.Rent(new(Small, CardTier.Bronze), "grid", "test"))
            .ToArray();
        backend.CompleteAll();
        rental.Tick();
        backend.CompleteAll();
        rental.Tick();

        foreach (var lease in leases)
            rental.Return(lease);

        Assert.Equal(1, rental.Pooled);
        Assert.Equal(2, rental.Destroyed);
        Assert.True(rental.IsBalanced);
    }

    [Fact]
    public void Release_all_destroys_held_and_pooled_cards_and_an_in_flight_one_when_it_arrives()
    {
        var backend = new FakeBackend();
        var rental = new CardRentalCore<FakeCard, string>(backend);
        var held = RentReady(rental, backend, new(Small, CardTier.Bronze));
        var pooled = RentReady(rental, backend, new(Small, CardTier.Bronze));
        rental.Return(pooled);
        var inFlight = rental.Rent(new(Medium, CardTier.Bronze), "grid", "test");

        Assert.Equal(2, rental.ReleaseAll());
        Assert.Equal(CardRentalLeaseState.Released, held.State);
        Assert.Equal(CardRentalLeaseState.Released, inFlight.State);
        Assert.Equal(0, rental.Pooled);
        Assert.Equal(2, rental.Destroyed);

        backend.CompleteAll();
        rental.Tick();

        Assert.Equal(3, rental.Instantiated);
        Assert.Equal(3, rental.Destroyed);
        Assert.Equal(0, rental.ActiveLeases);
        Assert.Equal(0, backend.Alive);
        Assert.True(rental.IsBalanced);
    }

    [Fact]
    public void Pooling_resumes_after_a_release_all_while_its_in_flight_card_is_still_destroyed()
    {
        var backend = new FakeBackend();
        var rental = new CardRentalCore<FakeCard, string>(backend);
        var before = rental.Rent(new(Small, CardTier.Bronze), "grid", "test");
        backend.CompleteAll();
        rental.Tick(); // `before` is now setting up
        rental.ReleaseAll();
        var after = rental.Rent(new(Medium, CardTier.Bronze), "grid", "test");
        backend.CompleteAll();
        rental.Tick(); // `before`'s setup ends: destroyed; `after` instantiated, setting up
        rental.Return(after); // returned during its setup

        backend.CompleteAll();
        rental.Tick();

        Assert.Equal(CardRentalLeaseState.Released, before.State);
        Assert.Equal(1, rental.Destroyed);
        Assert.Equal(1, rental.Pooled);
        Assert.True(rental.IsBalanced);
    }

    [Fact]
    public void A_retarget_during_setup_runs_after_that_setup_and_never_overlaps_it()
    {
        var backend = new FakeBackend();
        var rental = new CardRentalCore<FakeCard, string>(backend);
        var lease = RentReady(rental, backend, new(Small, CardTier.Bronze));

        rental.Retarget(lease, new(Small, CardTier.Silver));
        rental.Retarget(lease, new(Small, CardTier.Diamond));
        Assert.Equal(1, backend.PendingSetUps);
        backend.CompleteAll();
        rental.Tick();
        Assert.Equal(CardRentalLeaseState.Pending, lease.State);
        Assert.Equal(1, backend.PendingSetUps);
        backend.CompleteAll();
        rental.Tick();

        Assert.Equal(CardRentalLeaseState.Ready, lease.State);
        Assert.Equal(CardTier.Diamond, lease.Card!.SetUpTier);
        Assert.Equal(1, backend.MaxConcurrentSetUpsPerCard);
        Assert.Equal(1, rental.Instantiated);
        Assert.True(rental.IsBalanced);
    }

    [Fact]
    public void A_retarget_during_a_setup_spanning_several_frames_still_runs_when_it_ends()
    {
        var backend = new FakeBackend();
        var rental = new CardRentalCore<FakeCard, string>(backend);
        var lease = rental.Rent(new(Small, CardTier.Bronze), "grid", "test");
        backend.CompleteAll();
        rental.Tick(); // instantiated; the Bronze setup is now in flight

        rental.Retarget(lease, new(Small, CardTier.Diamond));
        rental.Tick(); // the Bronze setup is still running
        rental.Tick();
        Assert.Equal(CardRentalLeaseState.Pending, lease.State);
        backend.CompleteAll();
        rental.Tick(); // Bronze ends; the Diamond setup starts
        Assert.Equal(CardRentalLeaseState.Pending, lease.State);
        Assert.Equal(CardTier.Bronze, lease.Card!.SetUpTier);
        rental.Tick(); // the Diamond setup is still running
        Assert.Equal(CardRentalLeaseState.Pending, lease.State);
        backend.CompleteAll();
        rental.Tick();

        Assert.Equal(CardRentalLeaseState.Ready, lease.State);
        Assert.Equal(CardTier.Diamond, lease.Subject.Tier);
        Assert.Equal(CardTier.Diamond, lease.Card!.SetUpTier);
        Assert.Equal(1, lease.Revision);
        Assert.Equal(1, backend.MaxConcurrentSetUpsPerCard);
        Assert.Equal(0, backend.PendingSetUps);
        Assert.True(rental.IsBalanced);
    }

    [Fact]
    public void A_retarget_to_another_kind_pools_the_old_card_and_rents_a_new_one()
    {
        var backend = new FakeBackend();
        var rental = new CardRentalCore<FakeCard, string>(backend);
        var lease = RentReady(rental, backend, new(Small, CardTier.Bronze));

        rental.Retarget(lease, new(Medium, CardTier.Bronze));
        backend.CompleteAll();
        rental.Tick();
        backend.CompleteAll();
        rental.Tick();

        Assert.Equal(CardRentalLeaseState.Ready, lease.State);
        Assert.Equal("medium", lease.Card!.Kind);
        Assert.Equal(1, rental.Pooled);
        Assert.True(rental.IsBalanced);
    }

    [Fact]
    public void A_missing_template_fails_the_lease_without_an_instance()
    {
        var backend = new FakeBackend();
        var rental = new CardRentalCore<FakeCard, string>(backend);

        var lease = rental.Rent(new(Missing, CardTier.Bronze), "grid", "test");

        Assert.Equal(CardRentalLeaseState.Failed, lease.State);
        Assert.Equal("template_missing", lease.FailureReason);
        Assert.Equal(1, rental.Failed);
        Assert.Equal(0, rental.ActiveLeases);
        Assert.True(rental.IsBalanced);
    }

    [Fact]
    public void A_failed_setup_destroys_its_card_and_a_throwing_instantiate_fails_the_lease()
    {
        var backend = new FakeBackend { FailSetUps = true };
        var rental = new CardRentalCore<FakeCard, string>(backend);
        var lease = rental.Rent(new(Small, CardTier.Bronze), "grid", "test");
        backend.CompleteAll();
        rental.Tick();
        backend.CompleteAll();
        rental.Tick();

        Assert.Equal(CardRentalLeaseState.Failed, lease.State);
        Assert.Equal(1, rental.Destroyed);

        backend.ThrowOnInstantiate = true;
        var thrown = rental.Rent(new(Small, CardTier.Bronze), "grid", "test");
        Assert.Equal(CardRentalLeaseState.Failed, thrown.State);
        Assert.StartsWith("instantiate_threw:", thrown.FailureReason);
        Assert.Equal(2, rental.Failed);
        Assert.True(rental.IsBalanced);
    }

    [Fact]
    public void A_card_the_game_destroyed_counts_as_lost_and_is_never_reused()
    {
        var backend = new FakeBackend();
        var rental = new CardRentalCore<FakeCard, string>(backend);
        var lease = RentReady(rental, backend, new(Small, CardTier.Bronze));
        rental.Return(lease);
        backend.KillAll();

        var next = rental.Rent(new(Small, CardTier.Bronze), "grid", "test");
        backend.CompleteAll();
        rental.Tick();
        backend.CompleteAll();
        rental.Tick();

        Assert.Equal(CardRentalLeaseState.Ready, next.State);
        Assert.Equal(1, rental.Lost);
        Assert.Equal(0, rental.PoolHits);
        Assert.True(rental.IsBalanced);
    }

    private static CardRentalLease<FakeCard> RentReady(
        CardRentalCore<FakeCard, string> rental,
        FakeBackend backend,
        CardRentalSubject subject
    )
    {
        var lease = rental.Rent(subject, "grid", "test");
        for (var step = 0; step < 4 && lease.State == CardRentalLeaseState.Pending; step++)
        {
            backend.CompleteAll();
            rental.Tick();
        }
        Assert.Equal(CardRentalLeaseState.Ready, lease.State);
        return lease;
    }

    private sealed class FakeCard(string kind)
    {
        public string Kind { get; } = kind;
        public bool Alive { get; set; } = true;
        public string? Parent { get; set; }
        public CardTier? SetUpTier { get; set; }
        public int SetUpsInFlight { get; set; }
    }

    private sealed class Operation
    {
        public bool Done { get; set; }
        public FakeCard? Card { get; init; }
        public CardRentalSubject Subject { get; init; }
    }

    private sealed class FakeBackend : ICardRentalBackend<FakeCard, string>
    {
        private readonly List<Operation> _operations = new();
        private readonly List<FakeCard> _cards = new();

        public bool FailSetUps { get; set; }
        public bool ThrowOnInstantiate { get; set; }
        public int MaxConcurrentSetUpsPerCard { get; private set; }
        public int PendingSetUps => _operations.Count(op => !op.Done && op.Card != null);
        public int Alive => _cards.Count(card => card.Alive);

        public void CompleteAll()
        {
            foreach (var operation in _operations)
                operation.Done = true;
        }

        public void KillAll()
        {
            foreach (var card in _cards)
                card.Alive = false;
        }

        public string? ResolveKind(CardRentalSubject subject, out string? failureReason)
        {
            failureReason = null;
            if (subject.TemplateId == Small)
                return "small";
            if (subject.TemplateId == Medium)
                return "medium";
            failureReason = "template_missing";
            return null;
        }

        public object BeginInstantiate(CardRentalSubject subject, string kind)
        {
            if (ThrowOnInstantiate)
                throw new InvalidOperationException("loader missing");
            var operation = new Operation { Subject = subject };
            _operations.Add(operation);
            return operation;
        }

        public bool TryCompleteInstantiate(
            object operation,
            out FakeCard? card,
            out string? failureReason
        )
        {
            card = null;
            failureReason = null;
            var op = (Operation)operation;
            if (!op.Done)
                return false;
            _operations.Remove(op);
            card = new FakeCard(ResolveKind(op.Subject, out _)!);
            _cards.Add(card);
            return true;
        }

        public object BeginSetUp(FakeCard card, CardRentalSubject subject, string parent)
        {
            card.Parent = parent;
            card.SetUpsInFlight++;
            MaxConcurrentSetUpsPerCard = Math.Max(MaxConcurrentSetUpsPerCard, card.SetUpsInFlight);
            var operation = new Operation { Card = card, Subject = subject };
            _operations.Add(operation);
            return operation;
        }

        public bool TryCompleteSetUp(object operation, FakeCard card, out string? failureReason)
        {
            failureReason = null;
            var op = (Operation)operation;
            if (!op.Done)
                return false;
            _operations.Remove(op);
            card.SetUpsInFlight--;
            if (FailSetUps)
            {
                failureReason = "setup_faulted";
                return true;
            }
            card.SetUpTier = op.Subject.Tier;
            return true;
        }

        public void Park(FakeCard card) => card.Parent = "pool";

        public void Destroy(FakeCard card) => card.Alive = false;

        public bool IsAlive(FakeCard card) => card.Alive;
    }
}
