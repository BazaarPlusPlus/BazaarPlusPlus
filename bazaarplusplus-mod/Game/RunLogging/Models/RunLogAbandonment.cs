#nullable enable
using System;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;

namespace BazaarPlusPlus.Game.RunLogging.Models;

public sealed class RunLogAbandonment
{
    public int SchemaVersion { get; set; } = RunLogSqliteSchema.RowSchemaVersion;

    public string RunId { get; set; } = string.Empty;

    public string Status { get; set; } = "abandoned";

    public DateTimeOffset EndedAtUtc { get; set; }

    public int? FinalDay { get; set; }

    public int? FinalHour { get; set; }

    public string? Reason { get; set; }
}
