#nullable enable
using BazaarPlusPlus.Game.PvpBattles.Persistence;

namespace BazaarPlusPlus.Game.CombatReplay;

public readonly record struct ReplayPayloadRetentionSelection(
    IReadOnlyList<string> BattleIds,
    int EvaluatedCount
);

/// <summary>
/// Which Replay Payloads every host keeps: the newest <see cref="RetainedNewestCount"/>, those
/// recorded within <see cref="RetainedAge"/>, and any a caller protects; the rest are evicted.
/// </summary>
public static class ReplayPayloadRetentionPolicy
{
    // BazaarPlusPlus.history-database.json restates both values for the installer's V5 import;
    // ReplayPayloadRetentionPolicyTests keeps them equal.
    public const int RetainedNewestCount = 200;
    public static readonly TimeSpan RetainedAge = TimeSpan.FromDays(30);

    public static ReplayPayloadRetentionSelection SelectEvictionCandidates(
        IReadOnlyCollection<ReplayPayloadMaintenanceRecord> inventory,
        ISet<string> protectedBattleIds,
        DateTimeOffset now
    )
    {
        if (inventory == null)
            throw new ArgumentNullException(nameof(inventory));
        if (protectedBattleIds == null)
            throw new ArgumentNullException(nameof(protectedBattleIds));

        var ready = inventory
            .Where(record => record.PayloadState == ReplayPayloadState.Ready)
            .OrderByDescending(record => record.RecordedAtUtc)
            .ThenByDescending(record => record.BattleId, StringComparer.Ordinal)
            .ToList();
        var cutoff = now - RetainedAge;
        var candidates = new List<string>();
        for (var index = 0; index < ready.Count; index++)
        {
            var record = ready[index];
            if (
                index < RetainedNewestCount
                || record.RecordedAtUtc >= cutoff
                || protectedBattleIds.Contains(record.BattleId)
            )
            {
                continue;
            }

            candidates.Add(record.BattleId);
        }

        return new ReplayPayloadRetentionSelection(candidates, ready.Count);
    }
}
