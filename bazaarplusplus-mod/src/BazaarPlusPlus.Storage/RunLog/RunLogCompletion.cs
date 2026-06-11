#nullable enable
using System;

namespace BazaarPlusPlus.Storage.RunLog;

public sealed class RunLogCompletion
{
    public int SchemaVersion { get; set; } = RunLogSchema.RowSchemaVersion;

    public string RunId { get; set; } = string.Empty;

    public string Status { get; set; } = "completed";

    public DateTimeOffset EndedAtUtc { get; set; }

    public int? FinalDay { get; set; }

    public int? FinalHour { get; set; }

    public int? MaxHealth { get; set; }

    public int? Prestige { get; set; }

    public int? Level { get; set; }

    public int? Income { get; set; }

    public int? Gold { get; set; }

    public int? Victories { get; set; }

    public int? Losses { get; set; }

    public string? FinalPlayerRank { get; set; }

    public int? FinalPlayerRating { get; set; }

    public int? FinalPlayerRatingDelta { get; set; }

    public string? Reason { get; set; }
}
