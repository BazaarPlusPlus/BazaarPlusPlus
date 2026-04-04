#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles;

internal sealed class PvpReplayPayload
{
    public string BattleId { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public string SpawnMessageBase64 { get; set; } = string.Empty;

    public string CombatMessageBase64 { get; set; } = string.Empty;

    public string DespawnMessageBase64 { get; set; } = string.Empty;
}
