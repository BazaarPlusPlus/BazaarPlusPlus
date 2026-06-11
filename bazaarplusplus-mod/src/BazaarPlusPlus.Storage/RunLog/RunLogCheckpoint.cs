#nullable enable
using System;

namespace BazaarPlusPlus.Storage.RunLog;

public sealed class RunLogCheckpoint
{
    public int SchemaVersion { get; set; } = RunLogSchema.RowSchemaVersion;

    public string RunId { get; set; } = string.Empty;

    public long LastSeq { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public int? MaxHealth { get; set; }

    public int? Prestige { get; set; }

    public int? Level { get; set; }

    public int? Income { get; set; }

    public int? Gold { get; set; }

    public bool Completed { get; set; }
}
