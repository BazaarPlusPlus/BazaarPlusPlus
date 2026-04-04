#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;

namespace BazaarPlusPlus.Game.RunLogging.Models;

public sealed class RunLogEvent
{
    public int SchemaVersion { get; set; } = RunLogSqliteSchema.RowSchemaVersion;

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

    public string? SelectionFingerprint { get; set; }

    public IDictionary<string, object?> SelectionContextRules { get; set; } =
        new Dictionary<string, object?>();

    public IList<RunLogOptionSnapshot> Options { get; set; } = new List<RunLogOptionSnapshot>();

    public long? SelectionSeq { get; set; }

    public string? SelectedInstanceId { get; set; }

    public string? SelectedTemplateId { get; set; }

    public string? SelectedEncounterId { get; set; }

    public string? SelectedName { get; set; }

    public string? SelectedTier { get; set; }

    public string? SelectedEnchant { get; set; }

    public string? AbandonedReason { get; set; }

    public string? InferredFrom { get; set; }

    public double? Confidence { get; set; }

    public bool ShouldSerializeSelectionContextRules()
    {
        return SelectionContextRules.Count > 0;
    }

    public bool ShouldSerializeOptions()
    {
        return Options.Count > 0;
    }
}
