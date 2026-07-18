#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

public static class BazaarAgentTargetSelectionActions
{
    /// <summary>Lightweight value snapshot of an owned card, with everything the
    /// emitter needs to build a SelectItem decision option targeting it. Decoupled
    /// from game types so the helper stays unit-testable.</summary>
    public readonly record struct OwnedCardRef(
        string InstanceId,
        string TemplateId,
        BazaarAgentTargetSection Section,
        string LeftSocketId,
        int Size
    );

    /// <summary>Emit one SelectItem BazaarAgentDecisionOption per owned card whose
    /// TemplateId is in <paramref name="filter"/>. When <paramref name="filter"/>
    /// is empty, returns empty. Skill-section cards get no TargetSockets list.</summary>
    public static IReadOnlyList<BazaarAgentDecisionOption> Emit(
        ISet<string> filter,
        IEnumerable<OwnedCardRef> ownedCards
    )
    {
        if (filter.Count == 0)
            return System.Array.Empty<BazaarAgentDecisionOption>();
        var result = new List<BazaarAgentDecisionOption>();
        var seen = new HashSet<string>();
        foreach (var c in ownedCards)
        {
            if (string.IsNullOrEmpty(c.TemplateId))
                continue;
            if (!filter.Contains(c.TemplateId))
                continue;
            if (!seen.Add(c.InstanceId))
                continue;
            if (
                c.Section is not BazaarAgentTargetSection.Hand
                && c.Section is not BazaarAgentTargetSection.Stash
            )
            {
                continue;
            }

            IReadOnlyList<string>? sockets = null;
            if (
                !string.IsNullOrEmpty(c.LeftSocketId)
                && c.Size > 0
                && TryParseSocketIndex(c.LeftSocketId, out var start)
            )
            {
                var list = new string[c.Size];
                for (var i = 0; i < c.Size; i++)
                    list[i] = "Socket_" + (start + i);
                sockets = list;
            }

            var socketSeg = sockets is null ? "" : ":" + string.Join(",", sockets);
            result.Add(
                new BazaarAgentDecisionOption
                {
                    ActionKind = BazaarAgentActionKind.SelectItem,
                    Group = BazaarAgentActionGroup.Offer,
                    DisplayKey = "SelectItem:" + c.InstanceId + ":" + c.Section + socketSeg,
                    CardInstanceId = c.InstanceId,
                    TargetSection = c.Section,
                    TargetSockets = sockets,
                }
            );
        }
        return result;
    }

    private static bool TryParseSocketIndex(string socketId, out int index)
    {
        index = 0;
        if (string.IsNullOrEmpty(socketId))
            return false;
        const string prefix = "Socket_";
        if (!socketId.StartsWith(prefix, System.StringComparison.Ordinal))
            return false;
        return int.TryParse(socketId.Substring(prefix.Length), out index);
    }

    /// <summary>Target-selection mode (filter non-empty): preserve only the
    /// <c>SelectItem</c> options whose underlying templateId is in the filter (these
    /// are the only clicks the game accepts), then append owned-card SelectItem
    /// options for any owned card whose templateId is in the filter — covers the
    /// BuySpecificCardCondition._canInteractWithOwnedCards=true variant. Existing
    /// non-SelectItem options pass through untouched.</summary>
    public static IReadOnlyList<BazaarAgentDecisionOption> ApplyTargetSelectionFilter(
        IReadOnlyList<BazaarAgentDecisionOption> actions,
        ISet<string> filter,
        IReadOnlyList<BazaarAgentCardSnapshot> boardItems,
        IReadOnlyList<BazaarAgentCardSnapshot> chestItems,
        IReadOnlyList<BazaarAgentCardSnapshot> playerSkills,
        IReadOnlyList<BazaarAgentCardSnapshot> selectionOptionsCards
    )
    {
        var templateByInstance = new Dictionary<string, string>();
        AddTemplates(templateByInstance, boardItems);
        AddTemplates(templateByInstance, chestItems);
        AddTemplates(templateByInstance, playerSkills);
        AddTemplates(templateByInstance, selectionOptionsCards);

        var kept = new List<BazaarAgentDecisionOption>(actions.Count);
        var keptInstanceIds = new HashSet<string>();
        foreach (var a in actions)
        {
            if (a.ActionKind != BazaarAgentActionKind.SelectItem)
            {
                kept.Add(a);
                continue;
            }
            if (a.CardInstanceId is null)
                continue;
            if (!templateByInstance.TryGetValue(a.CardInstanceId, out var tid))
                continue;
            if (!filter.Contains(tid))
                continue;
            kept.Add(a);
            keptInstanceIds.Add(a.CardInstanceId);
        }

        var owned = new List<OwnedCardRef>();
        AddOwnedRefs(owned, boardItems, BazaarAgentTargetSection.Hand);
        AddOwnedRefs(owned, chestItems, BazaarAgentTargetSection.Stash);

        var targetOpts = Emit(filter, owned);
        foreach (var opt in targetOpts)
        {
            if (opt.CardInstanceId is null)
                continue;
            if (!keptInstanceIds.Add(opt.CardInstanceId))
                continue;
            kept.Add(opt);
        }
        return kept;
    }

    private static void AddTemplates(
        Dictionary<string, string> sink,
        IReadOnlyList<BazaarAgentCardSnapshot> cards
    )
    {
        foreach (var c in cards)
        {
            if (string.IsNullOrEmpty(c.InstanceId) || string.IsNullOrEmpty(c.TemplateId))
                continue;
            if (!sink.ContainsKey(c.InstanceId))
                sink[c.InstanceId] = c.TemplateId!;
        }
    }

    private static void AddOwnedRefs(
        List<OwnedCardRef> sink,
        IReadOnlyList<BazaarAgentCardSnapshot> cards,
        BazaarAgentTargetSection section
    )
    {
        foreach (var c in cards)
        {
            if (string.IsNullOrEmpty(c.TemplateId))
                continue;
            int size = BazaarAgentCardSize.Parse(c.Size, fallback: 1);
            sink.Add(
                new OwnedCardRef(
                    InstanceId: c.InstanceId,
                    TemplateId: c.TemplateId!,
                    Section: section,
                    LeftSocketId: c.SocketId ?? "",
                    Size: size
                )
            );
        }
    }
}
