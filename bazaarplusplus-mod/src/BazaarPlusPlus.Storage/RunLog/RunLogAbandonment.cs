#nullable enable
using System;

namespace BazaarPlusPlus.Storage.RunLog;

public sealed class RunLogAbandonment
{
    public int SchemaVersion { get; set; } = RunLogSchema.RowSchemaVersion;

    public string RunId { get; set; } = string.Empty;

    public string Status { get; set; } = "abandoned";

    public DateTimeOffset EndedAtUtc { get; set; }

    public int? FinalDay { get; set; }

    public int? FinalHour { get; set; }

    public string? Reason { get; set; }
}
