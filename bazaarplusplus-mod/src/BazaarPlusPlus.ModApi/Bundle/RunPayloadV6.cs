#nullable enable
using MessagePack;

namespace BazaarPlusPlus.ModApi.Bundle;

[MessagePackObject]
public sealed class RunPayloadV6
{
    [Key(0)]
    public int PayloadFormatVersion { get; set; } = 6;

    [Key(1)]
    public string RunId { get; set; } = string.Empty;

    [Key(2)]
    public string PlayerAccountId { get; set; } = string.Empty;

    [Key(3)]
    public RunFactsV6 Run { get; set; } = new();

    [Key(4)]
    public List<RunEventV6> Events { get; set; } = new();

    [Key(5)]
    public List<RunBattleV6> Battles { get; set; } = new();

    [Key(6)]
    public List<string> ReplayableBattleIds { get; set; } = new();

    [Key(7)]
    public PayloadDegradationV6 Degradation { get; set; } = new();
}

[MessagePackObject]
public sealed class RunFactsV6
{
    [Key(0)]
    public string Hero { get; set; } = string.Empty;

    [Key(1)]
    public string GameMode { get; set; } = string.Empty;

    [Key(2)]
    public int? Seed { get; set; }

    [Key(3)]
    public long StartedAtMs { get; set; }

    [Key(4)]
    public long? EndedAtMs { get; set; }

    [Key(5)]
    public string Status { get; set; } = string.Empty;

    [Key(6)]
    public int? Day { get; set; }

    [Key(7)]
    public int? Hour { get; set; }

    [Key(8)]
    public int? Victories { get; set; }

    [Key(9)]
    public int? Losses { get; set; }

    [Key(10)]
    public string? PlayerRank { get; set; }

    [Key(11)]
    public int? PlayerRating { get; set; }

    [Key(12)]
    public string? FinalPlayerRank { get; set; }

    [Key(13)]
    public int? FinalPlayerRating { get; set; }

    [Key(14)]
    public int? FinalPlayerRatingDelta { get; set; }

    [Key(15)]
    public int? MaxHealth { get; set; }

    [Key(16)]
    public int? Prestige { get; set; }

    [Key(17)]
    public int? Level { get; set; }

    [Key(18)]
    public int? Income { get; set; }

    [Key(19)]
    public int? Gold { get; set; }

    [Key(20)]
    public string? BuildChannel { get; set; }

    [Key(21)]
    public string ModVersion { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class RunEventV6
{
    [Key(0)]
    public long Seq { get; set; }

    [Key(1)]
    public long TimestampMs { get; set; }

    [Key(2)]
    public string Kind { get; set; } = string.Empty;

    [Key(3)]
    public string PayloadJson { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class RunBattleV6
{
    [Key(0)]
    public string BattleId { get; set; } = string.Empty;

    [Key(1)]
    public BattleFactsV6 Facts { get; set; } = new();

    [Key(2)]
    public BattleParticipantsV6 Participants { get; set; } = new();

    [Key(3)]
    public BattleCardSnapshotsV6? Snapshots { get; set; }

    [Key(4)]
    public BattleReplayV6? Replay { get; set; }
}

[MessagePackObject]
public sealed class BattleFactsV6
{
    [Key(0)]
    public long RecordedAtMs { get; set; }

    [Key(1)]
    public int? Day { get; set; }

    [Key(2)]
    public int? Hour { get; set; }

    [Key(3)]
    public string? EncounterId { get; set; }

    [Key(4)]
    public string CombatKind { get; set; } = string.Empty;

    [Key(5)]
    public string? Result { get; set; }

    [Key(6)]
    public string? WinnerCombatantId { get; set; }

    [Key(7)]
    public string? LoserCombatantId { get; set; }

    [Key(8)]
    public bool IsFinalBattle { get; set; }
}

[MessagePackObject]
public sealed class BattleParticipantsV6
{
    [Key(0)]
    public BattleParticipantV6 Player { get; set; } = new();

    [Key(1)]
    public BattleParticipantV6 Opponent { get; set; } = new();
}

[MessagePackObject]
public sealed class BattleParticipantV6
{
    [Key(0)]
    public string? AccountId { get; set; }

    [Key(1)]
    public string? DisplayName { get; set; }

    [Key(2)]
    public string? HeroName { get; set; }

    [Key(3)]
    public string? Rank { get; set; }

    [Key(4)]
    public int? Rating { get; set; }

    [Key(5)]
    public int? Level { get; set; }

    [Key(6)]
    public int? Prestige { get; set; }

    [Key(7)]
    public int? Victories { get; set; }

    [Key(8)]
    public int? Income { get; set; }

    [Key(9)]
    public int? Gold { get; set; }

    [Key(10)]
    public int? HandItemCount { get; set; }

    [Key(11)]
    public int? SkillCount { get; set; }
}

[MessagePackObject]
public sealed class BattleCardSnapshotsV6
{
    [Key(0)]
    public List<BattleCardSetV6> CardSets { get; set; } = new();
}

[MessagePackObject]
public sealed class BattleCardSetV6
{
    [Key(0)]
    public string Label { get; set; } = string.Empty;

    [Key(1)]
    public string? Status { get; set; }

    [Key(2)]
    public string? Source { get; set; }

    [Key(3)]
    public List<BattleCardV6> Items { get; set; } = new();
}

[MessagePackObject]
public sealed class BattleCardV6
{
    [Key(0)]
    public string InstanceId { get; set; } = string.Empty;

    [Key(1)]
    public string TemplateId { get; set; } = string.Empty;

    [Key(2)]
    public int Type { get; set; }

    [Key(3)]
    public int Size { get; set; }

    [Key(4)]
    public int? Section { get; set; }

    [Key(5)]
    public int? Socket { get; set; }

    [Key(6)]
    public string? Name { get; set; }

    [Key(7)]
    public string? Tier { get; set; }

    [Key(8)]
    public string? Enchant { get; set; }

    [Key(9)]
    public List<string> Tags { get; set; } = new();

    [Key(10)]
    public Dictionary<string, int> Attributes { get; set; } = new();
}

[MessagePackObject]
public sealed class BattleReplayV6
{
    [Key(0)]
    public int Version { get; set; } = 1;

    [Key(1)]
    public byte[] SpawnMessageBytes { get; set; } = Array.Empty<byte>();

    [Key(2)]
    public byte[] CombatMessageBytes { get; set; } = Array.Empty<byte>();

    [Key(3)]
    public byte[] DespawnMessageBytes { get; set; } = Array.Empty<byte>();
}

[MessagePackObject]
public sealed class PayloadDegradationV6
{
    [Key(0)]
    public List<string> Categories { get; set; } = new();

    [Key(1)]
    public List<string> ReplayOmittedBattleIds { get; set; } = new();

    [Key(2)]
    public int EventsOmitted { get; set; }

    [Key(3)]
    public bool ScreenshotOmitted { get; set; }
}
