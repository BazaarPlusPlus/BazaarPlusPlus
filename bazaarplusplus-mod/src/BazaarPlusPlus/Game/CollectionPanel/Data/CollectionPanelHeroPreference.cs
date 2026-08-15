#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal enum CollectionPanelHeroPreferenceLoadStatus
{
    Absent,
    Resolved,
    KnownUnavailable,
    Invalid,
}

internal sealed class CollectionPanelHeroPreferenceLoadResult
{
    public CollectionPanelHeroPreferenceLoadResult(
        CollectionPanelHeroPreferenceLoadStatus status,
        EHero? hero,
        string? canonicalRaw
    )
    {
        Status = status;
        Hero = hero;
        CanonicalRaw = canonicalRaw;
    }

    public CollectionPanelHeroPreferenceLoadStatus Status { get; }

    public EHero? Hero { get; }

    public string? CanonicalRaw { get; }
}

internal static class CollectionPanelHeroPreference
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

    public static string Serialize(EHero hero) =>
        TheDragonsHeroIdentity.TryCanonicalize(hero.ToString(), out var canonicalId)
            ? canonicalId
            : hero.ToString();

    public static bool TryParse(string? raw, out EHero hero)
    {
        hero = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (!TheDragonsHeroIdentity.TryResolve(raw.Trim(), out var parsed))
            return false;

        if (!IsSupportedHero(parsed))
            return false;

        hero = parsed;
        return true;
    }

    public static bool IsSupportedHero(EHero hero)
    {
        return hero == EHero.Common
            || CollectionHeroSelectionRoster.BaseConcreteHeroes.Contains(hero)
            || TheDragonsHeroIdentity.IsTheDragons(hero);
    }

    public static CollectionPanelHeroPreferenceLoadResult ResolveStored(
        bool hasStoredValue,
        string? raw,
        CollectionCatalogReadiness catalogReadiness,
        IReadOnlyCollection<EHero> availableHeroes
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
            hero != EHero.Common
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
