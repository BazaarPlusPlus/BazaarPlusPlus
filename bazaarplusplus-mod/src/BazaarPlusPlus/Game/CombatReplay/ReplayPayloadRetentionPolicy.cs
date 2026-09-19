#nullable enable
using BazaarPlusPlus.Game.PvpBattles.Persistence;

namespace BazaarPlusPlus.Game.CombatReplay;

internal readonly record struct ReplayPayloadRetentionSelection(
    IReadOnlyList<string> BattleIds,
    int EvaluatedCount
);

internal static class ReplayPayloadRetentionPolicy
{
    internal const int RetainedNewestCount = 200;
    internal static readonly TimeSpan RetainedAge = TimeSpan.FromDays(30);

    internal static ReplayPayloadRetentionSelection SelectEvictionCandidates(
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
            .Where(record =>
                record.HasLocalPayload && record.PayloadState == ReplayPayloadState.Ready
            )
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
