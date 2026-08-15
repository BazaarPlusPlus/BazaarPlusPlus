#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal enum CollectionSourceHeroParseStatus
{
    Resolved,
    KnownButUnavailable,
    Invalid,
}

internal readonly record struct CollectionSourceHeroParseResult(
    CollectionSourceHeroParseStatus Status,
    EHero? Hero
);

internal static class CollectionSourceHeroParser
{
    internal static CollectionSourceHeroParseResult Parse(string? value) =>
        Parse(value, TryResolveExactRuntimeName);

    internal static CollectionSourceHeroParseResult Parse(
        string? value,
        Func<string, EHero?> resolveExactName
    )
    {
        if (resolveExactName == null)
            throw new ArgumentNullException(nameof(resolveExactName));
        if (string.IsNullOrWhiteSpace(value))
            return new CollectionSourceHeroParseResult(
                CollectionSourceHeroParseStatus.Invalid,
                null
            );

        var trimmed = value.Trim();
        if (TheDragonsHeroIdentity.IsAlias(trimmed))
        {
            return TheDragonsHeroIdentity.TryResolve(trimmed, resolveExactName, out var dragons)
                ? new CollectionSourceHeroParseResult(
                    CollectionSourceHeroParseStatus.Resolved,
                    dragons
                )
                : new CollectionSourceHeroParseResult(
                    CollectionSourceHeroParseStatus.KnownButUnavailable,
                    null
                );
        }

        if (
            Enum.TryParse(trimmed, ignoreCase: true, out EHero hero)
            && Enum.IsDefined(typeof(EHero), hero)
        )
        {
            return new CollectionSourceHeroParseResult(
                CollectionSourceHeroParseStatus.Resolved,
                hero
            );
        }

        return new CollectionSourceHeroParseResult(CollectionSourceHeroParseStatus.Invalid, null);
    }

    private static EHero? TryResolveExactRuntimeName(string name)
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
