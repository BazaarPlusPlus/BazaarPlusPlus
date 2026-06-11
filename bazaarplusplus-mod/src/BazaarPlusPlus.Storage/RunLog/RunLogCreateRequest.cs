#nullable enable
using System;

namespace BazaarPlusPlus.Storage.RunLog;

public sealed class RunLogCreateRequest
{
    public int SchemaVersion { get; set; } = RunLogSchema.RowSchemaVersion;

    public string RunId { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    public string Hero { get; set; } = string.Empty;

    public string GameMode { get; set; } = string.Empty;

    public string? PlayerRank { get; set; }

    public int? PlayerRating { get; set; }

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public int? Seed { get; set; }

    public string Status { get; set; } = "active";
}
