#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Core.GameState;

public readonly struct EncounterTargetingSnapshot
{
    public IReadOnlyList<string> InteractionFilterTemplateIds { get; init; }
    public HashSet<string> PedestalEligibleInstanceIds { get; init; }
    public bool IsPedestalState { get; init; }
    public bool IsTargetSelectionActive => InteractionFilterTemplateIds.Count > 0;

    public static EncounterTargetingSnapshot Empty =>
        new()
        {
            InteractionFilterTemplateIds = Array.Empty<string>(),
            PedestalEligibleInstanceIds = new HashSet<string>(),
            IsPedestalState = false,
        };
}
