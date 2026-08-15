#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.Game.CombatReplay.PlaybackUi;

internal static class CombatReplayHeroIdentity
{
    internal static bool TryParse(string? heroId, out EHero hero) =>
        TryParse(heroId, TryResolveExactEnumName, out hero);

    internal static bool TryParse(
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
        if (TheDragonsHeroIdentity.IsAlias(trimmed))
            return TheDragonsHeroIdentity.TryResolve(trimmed, resolveExactName, out hero);

        return Enum.TryParse(trimmed, ignoreCase: true, out hero);
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
}
