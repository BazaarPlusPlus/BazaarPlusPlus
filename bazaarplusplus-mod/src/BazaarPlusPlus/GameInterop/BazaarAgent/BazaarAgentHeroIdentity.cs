#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.GameInterop;

internal static class BazaarAgentHeroIdentity
{
    internal static BazaarAgentHeroResolution Resolve(string? heroId) =>
        Resolve(heroId, TryResolveExactEnumName);

    internal static BazaarAgentHeroResolution Resolve(
        string? heroId,
        Func<string, EHero?> resolveExactName
    )
    {
        if (resolveExactName == null)
            throw new ArgumentNullException(nameof(resolveExactName));
        if (string.IsNullOrWhiteSpace(heroId))
            return new(BazaarAgentHeroResolutionStatus.Invalid, default);

        var trimmed = heroId.Trim();
        if (TheDragonsHeroIdentity.IsAlias(trimmed))
        {
            return TheDragonsHeroIdentity.TryResolve(trimmed, resolveExactName, out var hero)
                ? new(BazaarAgentHeroResolutionStatus.Resolved, hero)
                : new(BazaarAgentHeroResolutionStatus.Unavailable, default);
        }

        return
            Enum.TryParse(trimmed, ignoreCase: true, out EHero parsed)
            && Enum.IsDefined(typeof(EHero), parsed)
            ? new(BazaarAgentHeroResolutionStatus.Resolved, parsed)
            : new(BazaarAgentHeroResolutionStatus.Invalid, default);
    }

    internal static string ToAgentContextId(EHero hero) =>
        TheDragonsHeroIdentity.IsTheDragons(hero)
            ? TheDragonsHeroIdentity.CanonicalId
            : hero.ToString();

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
}
