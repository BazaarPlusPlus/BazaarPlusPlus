#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.Supporters;

namespace BazaarPlusPlus.Game.CollectionPanel;

// What a host read from the game and its own caches when the Collection Panel opens, handed to
// CollectionViewState.Open. Values only; no Unity or IL2CPP types. Built only through InRun and
// OutOfRun, so an out-of-run open cannot carry a hero, day or encounter by mistake.
public sealed class CollectionOpenFacts
{
    private CollectionOpenFacts(
        bool isInGameRun,
        CardHero? hero,
        int? runDay,
        EncounterIdsSnapshot encounter,
        IReadOnlyList<CollectionCardVm>? cachedCatalog,
        IReadOnlyList<BPPSupporterSample> supporters
    )
    {
        IsInGameRun = isInGameRun;
        Hero = hero;
        RunDay = runDay;
        Encounter = encounter;
        CachedCatalog = cachedCatalog;
        Supporters = supporters ?? throw new ArgumentNullException(nameof(supporters));
    }

    // hero: the host's concrete-hero read, else null. encounter: a host with no encounter probe
    // passes EncounterIdsSnapshot.Empty. cachedCatalog: null while the host's catalog is building.
    public static CollectionOpenFacts InRun(
        CardHero? hero,
        int? runDay,
        EncounterIdsSnapshot encounter,
        IReadOnlyList<CollectionCardVm>? cachedCatalog,
        IReadOnlyList<BPPSupporterSample> supporters
    ) => new(true, hero, runDay, encounter, cachedCatalog, supporters);

    public static CollectionOpenFacts OutOfRun(
        IReadOnlyList<CollectionCardVm>? cachedCatalog,
        IReadOnlyList<BPPSupporterSample> supporters
    ) => new(false, null, null, EncounterIdsSnapshot.Empty, cachedCatalog, supporters);

    public bool IsInGameRun { get; }

    // Null out of a run.
    public CardHero? Hero { get; }

    // Null out of a run.
    public int? RunDay { get; }

    // Empty out of a run.
    public EncounterIdsSnapshot Encounter { get; }

    public IReadOnlyList<CollectionCardVm>? CachedCatalog { get; }

    public IReadOnlyList<BPPSupporterSample> Supporters { get; }
}
