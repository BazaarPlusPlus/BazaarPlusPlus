#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel;

public interface ICollectionPanelHeroPreferenceStore
{
    CollectionPanelHeroPreferenceLoadResult Load(
        CollectionCatalogReadiness catalogReadiness,
        IReadOnlyCollection<CardHero> availableHeroes
    );

    /// <summary>Stages <paramref name="hero"/>; it reaches disk on the next <see cref="Flush"/>.</summary>
    void Save(CardHero hero);

    void Flush();
}
