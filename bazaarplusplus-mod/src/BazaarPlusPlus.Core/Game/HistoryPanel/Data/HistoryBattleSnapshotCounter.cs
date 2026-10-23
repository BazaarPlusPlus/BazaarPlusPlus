#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.HistoryPanel.Data;

/// <summary>
/// A battle's board counts as its History row chips show them. They match what the host's board
/// preview renders: a card counts only with a parseable template id, the capture's card type (an
/// item in a hand, a skill in a skill set; a socket effect in a hand does not count), and a
/// template the game's static data knows, which only the host can answer.
/// </summary>
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
