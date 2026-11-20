#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.Supporters;

namespace BazaarPlusPlus.Game.CollectionPanel;

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

    public CardHero? Hero { get; }

    public int? RunDay { get; }

    public EncounterIdsSnapshot Encounter { get; }

    public IReadOnlyList<CollectionCardVm>? CachedCatalog { get; }

    public IReadOnlyList<BPPSupporterSample> Supporters { get; }
}
