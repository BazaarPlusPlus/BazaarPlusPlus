#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.HistoryPanel.Data;

/// <summary>Use the same card eligibility as the board preview so the chips count only cards the Host can actually render.</summary>
public static class HistoryBattleSnapshotCounter
{
    public static HistoryBattleSnapshotCounts Count(
        PvpBattleSnapshots snapshots,
        Func<Guid, bool> hasCardTemplate
    )
    {
        if (snapshots == null)
            throw new ArgumentNullException(nameof(snapshots));
        if (hasCardTemplate == null)
            throw new ArgumentNullException(nameof(hasCardTemplate));
        return new HistoryBattleSnapshotCounts(
            CountRenderable(snapshots.PlayerHand?.Items, CardType.Item, hasCardTemplate),
            CountRenderable(snapshots.PlayerSkills?.Items, CardType.Skill, hasCardTemplate),
            CountRenderable(snapshots.OpponentHand?.Items, CardType.Item, hasCardTemplate),
            CountRenderable(snapshots.OpponentSkills?.Items, CardType.Skill, hasCardTemplate)
        );
    }

    private static int CountRenderable(
        IList<PvpBattleCardSnapshot>? snapshots,
        CardType type,
        Func<Guid, bool> hasCardTemplate
    )
    {
        if (snapshots == null)
            return 0;

        var count = 0;
        foreach (var snapshot in snapshots)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TemplateId))
                continue;
            if (snapshot.Type != type)
                continue;
            if (!Guid.TryParse(snapshot.TemplateId, out var templateId))
                continue;
            if (templateId == Guid.Empty || !hasCardTemplate(templateId))
                continue;
            count++;
        }
        return count;
    }
}
