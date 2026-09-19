#nullable enable
namespace BazaarPlusPlus.Storage.RunLog;

public sealed class RunLogEvent
{
    public int SchemaVersion { get; set; } = RunLogSchema.RowSchemaVersion;

    public string RunId { get; set; } = string.Empty;

    public long Seq { get; set; }

    public DateTimeOffset Ts { get; set; }

    public string Kind { get; set; } = string.Empty;

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? Hero { get; set; }

    public string? GameMode { get; set; }

    public int? Victories { get; set; }

    public int? Losses { get; set; }

    public string? State { get; set; }

    public string? EncounterId { get; set; }

    public string? CombatKind { get; set; }

    public string? BattleId { get; set; }

    public string? OpponentName { get; set; }
}
