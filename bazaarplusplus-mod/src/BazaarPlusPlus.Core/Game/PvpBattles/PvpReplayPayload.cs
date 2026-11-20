#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles;

public sealed class PvpReplayPayload
{
    public string BattleId { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public byte[] SpawnMessageBytes { get; set; } = [];

    public byte[] CombatMessageBytes { get; set; } = [];

    public byte[] DespawnMessageBytes { get; set; } = [];

    // Replay attributes describe the recording player at combat open and use game member names as keys.
    public Dictionary<string, int>? PlayerAttributes { get; set; }

    // Stash cards are captured at combat open because their quests and auras can affect replay.
    public List<PvpBattleCardDocument>? PlayerStash { get; set; }
}
