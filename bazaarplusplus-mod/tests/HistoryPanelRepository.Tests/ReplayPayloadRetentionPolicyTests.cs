#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles.Persistence;

internal static class ReplayPayloadRetentionPolicyTests
{
    internal static void Run()
    {
        KeepsNewestCountUnionAgeWindowAndProtectedPayloads();
        UsesBattleIdAsStableTieBreak();
    }

    private static void KeepsNewestCountUnionAgeWindowAndProtectedPayloads()
    {
        var now = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);
        var records = Enumerable
            .Range(0, 205)
            .Select(index => Ready($"old-{index:D3}", now.AddDays(-31).AddMinutes(-index)))
            .ToList();
        records.Add(Ready("age-boundary", now.AddDays(-30)));
        records.Add(Ready("inside-age-window", now.AddDays(-29)));

        var result = ReplayPayloadRetentionPolicy.SelectEvictionCandidates(
            records,
            new HashSet<string>(["old-202"], StringComparer.Ordinal),
            now
        );

        Assert(
            result.BattleIds.SequenceEqual([
                "old-198",
                "old-199",
                "old-200",
                "old-201",
                "old-203",
                "old-204",
            ]),
            "The keep set must be newest 200 union last 30 days plus protected payloads."
        );
        Equal(records.Count, result.EvaluatedCount, "evaluated work units");
    }

    private static void UsesBattleIdAsStableTieBreak()
    {
        var now = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);
        var timestamp = now.AddDays(-31);
        var records = Enumerable
            .Range(0, 201)
            .Select(index => Ready($"battle-{index:D3}", timestamp))
            .ToList();

        var result = ReplayPayloadRetentionPolicy.SelectEvictionCandidates(
            records,
            new HashSet<string>(StringComparer.Ordinal),
            now
        );

        Assert(
            result.BattleIds.SequenceEqual(["battle-000"]),
            "Equal timestamps must retain lexicographically greater battle ids first."
        );
    }

    private static ReplayPayloadMaintenanceRecord Ready(
        string battleId,
        DateTimeOffset recordedAt
    ) => new(battleId, recordedAt, HasLocalPayload: true, ReplayPayloadState.Ready);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"Expected {label} to be '{expected}', got '{actual}'."
            );
    }
}
