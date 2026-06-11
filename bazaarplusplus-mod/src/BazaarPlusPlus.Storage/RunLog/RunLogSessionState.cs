#nullable enable
using System;

namespace BazaarPlusPlus.Storage.RunLog;

public sealed class RunLogSessionState
{
    public string RunId { get; set; } = string.Empty;

    public int SchemaVersion { get; set; } = RunLogSchema.RowSchemaVersion;

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }

    public long LastSeq { get; set; }

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public int? MaxHealth { get; set; }

    public int? Prestige { get; set; }

    public int? Level { get; set; }

    public int? Income { get; set; }

    public int? Gold { get; set; }

    public bool Completed { get; set; }
}
