#nullable enable
using System.Security.Cryptography;
using System.Text.Json;
using BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactProjectionBenchmark
{
    internal static CombatImpactProjectionBenchmarkObservation Observe(
        ReplayObservationInput replay,
        CombatImpactReport report,
        TimeSpan elapsed,
        long allocatedBytes
    )
    {
        var audit = report.CriticalTriggerEvidenceAudit;
        return new CombatImpactProjectionBenchmarkObservation(
            replay.BattleId,
            replay.Combat.Frames.Count,
            audit.InputEventCount,
            audit.InputExecutionCount,
            audit.ObservedEvidenceCount,
            audit.WorkUnitCount,
            elapsed.TotalMilliseconds,
            allocatedBytes,
            SemanticHash(report)
        );
    }

    internal static CombatImpactProjectionBenchmarkReport Summarize(
        IReadOnlyList<CombatImpactProjectionBenchmarkObservation> observations
    )
    {
        var elapsed = observations
            .Select(observation => observation.ElapsedMilliseconds)
            .Order()
            .ToArray();
        var allocated = observations
            .Select(observation => observation.AllocatedBytes)
            .Order()
            .ToArray();
        return new CombatImpactProjectionBenchmarkReport(
            observations.Count,
            observations.Sum(observation => observation.Frames),
            observations.Sum(observation => observation.Events),
            observations.Sum(observation => observation.Executions),
            observations.Sum(observation => observation.Evidence),
            observations.Sum(observation => observation.CriticalWorkUnits),
            elapsed.Sum(),
            Percentile(elapsed, 0.50),
            Percentile(elapsed, 0.95),
            elapsed.Length == 0 ? 0 : elapsed[^1],
            allocated.Sum(),
            Percentile(allocated, 0.50),
            Percentile(allocated, 0.95),
            allocated.Length == 0 ? 0 : allocated[^1],
            observations
        );
    }

    private static string SemanticHash(CombatImpactReport report)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile) =>
        sorted.Count == 0 ? 0 : sorted[PercentileIndex(sorted.Count, percentile)];

    private static long Percentile(IReadOnlyList<long> sorted, double percentile) =>
        sorted.Count == 0 ? 0 : sorted[PercentileIndex(sorted.Count, percentile)];

    private static int PercentileIndex(int count, double percentile) =>
        Math.Clamp((int)Math.Ceiling(count * percentile) - 1, 0, count - 1);
}

internal sealed record CombatImpactProjectionBenchmarkReport(
    int Battles,
    int Frames,
    int Events,
    int Executions,
    int Evidence,
    long CriticalWorkUnits,
    double ElapsedTotalMilliseconds,
    double ElapsedP50Milliseconds,
    double ElapsedP95Milliseconds,
    double ElapsedMaxMilliseconds,
    long AllocatedTotalBytes,
    long AllocatedP50Bytes,
    long AllocatedP95Bytes,
    long AllocatedMaxBytes,
    IReadOnlyList<CombatImpactProjectionBenchmarkObservation> PerBattle
);

internal sealed record CombatImpactProjectionBenchmarkObservation(
    string BattleId,
    int Frames,
    int Events,
    int Executions,
    int Evidence,
    long CriticalWorkUnits,
    double ElapsedMilliseconds,
    long AllocatedBytes,
    string SemanticSha256
);
