#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

public enum CollectionPanelHeroPreferenceLoadStatus
{
    Absent,
    Resolved,
    KnownUnavailable,
    Invalid,
}

public sealed class CollectionPanelHeroPreferenceLoadResult
{
    public CollectionPanelHeroPreferenceLoadResult(
        CollectionPanelHeroPreferenceLoadStatus status,
        CardHero? hero,
        string? canonicalRaw
    )
    {
        Status = status;
        Hero = hero;
        CanonicalRaw = canonicalRaw;
    }

    public CollectionPanelHeroPreferenceLoadStatus Status { get; }

    public CardHero? Hero { get; }

    public string? CanonicalRaw { get; }
}

public static class CollectionPanelHeroPreference
{
    private const string AnonymousAccountScope = "anonymous";
    private const string PrefsKeyPrefix = "BPP.CollectionPanel.SelectedHero";

    public static string BuildPrefsKey(string? accountScope)
    {
        var scope = string.IsNullOrWhiteSpace(accountScope)
            ? AnonymousAccountScope
            : Uri.EscapeDataString(accountScope);
        return $"{PrefsKeyPrefix}.{scope}";
    }

    // A hero facet's name is its canonical id (ADR-0015).
    public static string Serialize(CardHero hero) => hero.ToString();

    public static bool TryParse(string? raw, out CardHero hero)
    {
        hero = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw!.Trim();
        CardHero parsed;
        if (TheDragonsHeroId.IsAlias(trimmed))
            parsed = CardHero.TheDragons;
        else if (
            !Enum.TryParse(trimmed, ignoreCase: false, out parsed)
            || !Enum.IsDefined(typeof(CardHero), parsed)
        )
            return false;

        if (!IsSupportedHero(parsed))
            return false;

        hero = parsed;
        return true;
    }

    public static bool IsSupportedHero(CardHero hero)
    {
        return hero == CardHero.Common
            || CollectionHeroSelectionRoster.BaseConcreteHeroes.Contains(hero)
            || hero == CardHero.TheDragons;
    }

    public static CollectionPanelHeroPreferenceLoadResult ResolveStored(
        bool hasStoredValue,
        string? raw,
        CollectionCatalogReadiness catalogReadiness,
        IReadOnlyCollection<CardHero> availableHeroes
    )
    {
        if (availableHeroes == null)
            throw new ArgumentNullException(nameof(availableHeroes));
        if (!hasStoredValue)
        {
            return new CollectionPanelHeroPreferenceLoadResult(
                CollectionPanelHeroPreferenceLoadStatus.Absent,
                null,
                null
            );
        }

        if (!TryParse(raw, out var hero))
        {
            return new CollectionPanelHeroPreferenceLoadResult(
                CollectionPanelHeroPreferenceLoadStatus.Invalid,
                null,
                null
            );
        }

        var canonicalRaw = Serialize(hero);
        if (
            hero != CardHero.Common
            && catalogReadiness == CollectionCatalogReadiness.Accepted
            && !availableHeroes.Contains(hero)
        )
        {
            return new CollectionPanelHeroPreferenceLoadResult(
                CollectionPanelHeroPreferenceLoadStatus.KnownUnavailable,
                hero,
                canonicalRaw
            );
        }

        return new CollectionPanelHeroPreferenceLoadResult(
            CollectionPanelHeroPreferenceLoadStatus.Resolved,
            hero,
            canonicalRaw
        );
    }
}
