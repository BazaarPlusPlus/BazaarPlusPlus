#nullable enable
using BazaarGameShared.Domain.Core.Types;
using TheBazaar.Utilities;

namespace BazaarPlusPlus.GameInterop.Heroes;

/// <summary>
/// Temporary compatibility boundary for the game's The Dragons enum-name transition. Delete this
/// adapter once every supported game build exposes the canonical enum name.
/// </summary>
internal static class TheDragonsHeroIdentity
{
    internal const string CanonicalId = "TheDragons";
    internal const string FallbackDisplayName = "The Dragons";
    private const string LegacyId = "Hero8";
    private static readonly DisplayNameResolver NativeDisplayNames = new(
        LocalizableShared.GetLocalizedHero
    );

    internal static bool IsAlias(string? heroId) =>
        !string.IsNullOrWhiteSpace(heroId)
        && (
            string.Equals(heroId.Trim(), LegacyId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(heroId.Trim(), CanonicalId, StringComparison.OrdinalIgnoreCase)
        );

    internal static bool IsTheDragons(EHero hero) => IsAlias(hero.ToString());

    internal static string ToCanonicalId(EHero hero) => ToCanonicalId(hero, RuntimeEnumName);

    /// <summary>
    /// Canonical id for anything that leaves the process (SQLite rows, uploaded payloads). The
    /// runtime enum name is injectable so tests can pin the result under either transitional build.
    /// </summary>
    internal static string ToCanonicalId(EHero hero, Func<EHero, string> getEnumName)
    {
        if (getEnumName == null)
            throw new ArgumentNullException(nameof(getEnumName));

        var name = getEnumName(hero);
        return IsAlias(name) ? CanonicalId : name;
    }

    internal static string CanonicalizeForStorage(string? heroId)
    {
        if (string.IsNullOrWhiteSpace(heroId))
            return string.Empty;

        var trimmed = heroId.Trim();
        return IsAlias(trimmed) ? CanonicalId : trimmed;
    }

    internal static IReadOnlyList<string> PersistenceReadIds(EHero hero) =>
        IsTheDragons(hero) ? [CanonicalId, LegacyId] : [hero.ToString()];

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
            // Prefer the canonical runtime member when a transitional build exposes both names.
            // Both input aliases intentionally take the same route so equivalence stays symmetric.
            resolved = resolveExactName(CanonicalId) ?? resolveExactName(LegacyId);
        }
        else
        {
            // Preserve normal EHero parsing: non-Dragons enum names remain case-sensitive.
            resolved = resolveExactName(trimmed);
        }

        if (!resolved.HasValue || (IsAlias(trimmed) && !IsTheDragons(resolved.Value)))
            return false;

        hero = resolved.Value;
        return true;
    }

    internal static bool TryCanonicalize(string? heroId, out string canonicalId)
    {
        canonicalId = string.Empty;
        if (IsAlias(heroId))
        {
            canonicalId = CanonicalId;
            return true;
        }

        if (!TryResolve(heroId, out var hero))
            return false;

        canonicalId = hero.ToString();
        return true;
    }

    internal static bool AreEquivalent(string? left, string? right) =>
        TryCanonicalize(left, out var leftCanonical)
        && TryCanonicalize(right, out var rightCanonical)
        && string.Equals(leftCanonical, rightCanonical, StringComparison.Ordinal);

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
