#nullable enable
using System;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal class RunSummaryUploadPayload
{
    public int SchemaVersion { get; set; } = RunLogSqliteSchema.UploadPayloadSchemaVersion;

    public string InstallId { get; set; } = string.Empty;

    public string? ClientId { get; set; }

    public string PluginVersion { get; set; } = string.Empty;

    public DateTimeOffset SubmittedAtUtc { get; set; }

    public string RunId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? HeroId { get; set; }

    public string? HeroName { get; set; }

    public string? StartedAtUtc { get; set; }

    public string EndedAtUtc { get; set; } = string.Empty;

    public int? FinalDay { get; set; }

    public int? FinalWins { get; set; }

    public int? FinalLosses { get; set; }

    public int? Mmr { get; set; }
}

internal class RunSummaryUploadSnapshot
{
    public RunSummaryUploadPayload Payload { get; set; } = new();

    public long LastSeq { get; set; }

    public string? UploadedStatus { get; set; }
}

internal readonly struct RunSummaryUploadCycleResult
{
    public RunSummaryUploadCycleResult(int uploadedCount, bool hasMorePending)
    {
        UploadedCount = uploadedCount;
        HasMorePending = hasMorePending;
    }

    public int UploadedCount { get; }

    public bool HasMorePending { get; }
}
