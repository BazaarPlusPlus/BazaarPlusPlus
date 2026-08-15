#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop;

public enum BazaarAgentHeroResolutionStatus
{
    Resolved,
    Invalid,
    Unavailable,
}

public readonly record struct BazaarAgentHeroResolution(
    BazaarAgentHeroResolutionStatus Status,
    EHero Hero
);
