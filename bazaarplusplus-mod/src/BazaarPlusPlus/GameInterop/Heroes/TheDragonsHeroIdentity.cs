#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.GameState;
using TheBazaar.Utilities;

namespace BazaarPlusPlus.GameInterop.Heroes;

// Supported builds expose different enum names for The Dragons.
internal static class TheDragonsHeroIdentity
{
    internal const string CanonicalId = TheDragonsHeroId.CanonicalId;
    internal const string FallbackDisplayName = "The Dragons";
    private const string LegacyId = TheDragonsHeroId.LegacyId;
    private static readonly DisplayNameResolver NativeDisplayNames = new(
        LocalizableShared.GetLocalizedHero
    );

    internal static bool IsAlias(string? heroId) => TheDragonsHeroId.IsAlias(heroId);

    internal static bool IsTheDragons(EHero hero) => IsAlias(hero.ToString());

    internal static string ToCanonicalId(EHero hero) => ToCanonicalId(hero, RuntimeEnumName);

    // Use one ID across builds in SQLite rows and uploaded payloads.
    internal static string ToCanonicalId(EHero hero, Func<EHero, string> getEnumName)
    {
        if (getEnumName == null)
            throw new ArgumentNullException(nameof(getEnumName));

        var name = getEnumName(hero);
        return IsAlias(name) ? CanonicalId : name;
    }

    internal static bool TryResolve(string? heroId, out EHero hero) =>
        TryResolve(heroId, TryResolveExactEnumName, out hero);

    internal static bool TryResolve(
        string? heroId,
        Func<string, EHero?> resolveExactName,
        out EHero hero
    )
    {
        if (resolveExactName == null)
            throw new ArgumentNullException(nameof(resolveExactName));

        hero = default;
        if (string.IsNullOrWhiteSpace(heroId))
            return false;

        var trimmed = heroId.Trim();
        EHero? resolved;
        if (IsAlias(trimmed))
        {
            // Resolve both aliases through the same path to preserve symmetric equivalence.
            resolved = resolveExactName(CanonicalId) ?? resolveExactName(LegacyId);
        }
        else
        {
            resolved = resolveExactName(trimmed);
        }

        if (!resolved.HasValue || (IsAlias(trimmed) && !IsTheDragons(resolved.Value)))
            return false;

        hero = resolved.Value;
        return true;
    }

    internal static string ResolveDisplayName(EHero hero) => NativeDisplayNames.Resolve(hero);

    internal static string ResolveDisplayName(EHero hero, Func<EHero, string?> getNativeName) =>
        new DisplayNameResolver(getNativeName).Resolve(hero);

    internal sealed class DisplayNameResolver
    {
        private readonly Func<EHero, string?> _getNativeName;
        private readonly HashSet<EHero> _stableAliasPlaceholderFallbacks = new();

        internal DisplayNameResolver(Func<EHero, string?> getNativeName)
        {
            _getNativeName =
                getNativeName ?? throw new ArgumentNullException(nameof(getNativeName));
        }

        internal string Resolve(EHero hero)
        {
            lock (_stableAliasPlaceholderFallbacks)
            {
                if (_stableAliasPlaceholderFallbacks.Contains(hero))
                    return FallbackFor(hero);

                string? nativeName;
                try
                {
                    nativeName = _getNativeName(hero);
                }
                catch (Exception)
                {
                    return FallbackFor(hero);
                }

                if (string.IsNullOrWhiteSpace(nativeName))
                    return FallbackFor(hero);

                if (!IsTheDragons(hero) || !IsAlias(nativeName))
                    return nativeName;

                _stableAliasPlaceholderFallbacks.Add(hero);
                return FallbackDisplayName;
            }
        }
    }

    private static EHero? TryResolveExactEnumName(string name)
    {
        if (
            !Enum.TryParse(name, ignoreCase: false, out EHero hero)
            || !Enum.IsDefined(typeof(EHero), hero)
        )
        {
            return null;
        }

        return hero;
    }

    private static string RuntimeEnumName(EHero hero) => hero.ToString();

    private static string FallbackFor(EHero hero) =>
        IsTheDragons(hero) ? FallbackDisplayName : hero.ToString();
}
