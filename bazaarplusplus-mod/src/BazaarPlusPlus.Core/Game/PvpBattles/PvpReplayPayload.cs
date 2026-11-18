#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles;

public sealed class PvpReplayPayload
{
    public string BattleId { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public byte[] SpawnMessageBytes { get; set; } = [];

    public byte[] CombatMessageBytes { get; set; } = [];

    public byte[] DespawnMessageBytes { get; set; } = [];

    /// <summary>
    /// The recording player's attributes as the combat opened, keyed by the game's member name;
    /// null in a payload recorded without them.
    /// </summary>
    public Dictionary<string, int>? PlayerAttributes { get; set; }
}
