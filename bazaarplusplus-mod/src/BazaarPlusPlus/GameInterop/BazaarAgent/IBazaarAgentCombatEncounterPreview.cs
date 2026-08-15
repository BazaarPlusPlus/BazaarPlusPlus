#nullable enable
using BazaarGameClient.Domain.Models.Cards;

namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// Reads the same static monster lineup shown by the native right-click combat-encounter
/// tooltip. The data is available before a PvE battle starts.
/// </summary>
public interface IBazaarAgentCombatEncounterPreview
{
    BazaarAgentCombatEncounterPreviewSnapshot? Resolve(CombatEncounterCard encounter);
}

public sealed class BazaarAgentCombatEncounterPreviewSnapshot
{
    public IReadOnlyList<BazaarAgentBattleCardSnapshot> Board { get; init; } =
        Array.Empty<BazaarAgentBattleCardSnapshot>();

    public IReadOnlyList<BazaarAgentBattleCardSnapshot> Skills { get; init; } =
        Array.Empty<BazaarAgentBattleCardSnapshot>();

    /// <summary>Initial monster health, when the template supplies it.</summary>
    public int? Health { get; init; }

    public int? MaxHealth { get; init; }
}
