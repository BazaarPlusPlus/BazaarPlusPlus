#nullable enable
using System;

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

    public int? CurrentHourXp { get; set; }

    public string? State { get; set; }

    public string? EncounterId { get; set; }

    public string? ParentEncounterId { get; set; }

    public string? CombatKind { get; set; }

    public string? BattleId { get; set; }

    public string? OpponentName { get; set; }

    public int? RerollCost { get; set; }

    public int? RerollsRemaining { get; set; }

    public string? StateFingerprint { get; set; }

    public string? AbandonedReason { get; set; }

    public string? InferredFrom { get; set; }

    public double? Confidence { get; set; }
}
