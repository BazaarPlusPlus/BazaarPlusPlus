#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Domain.Targeting;

namespace BazaarPlusPlus.Game.MusicNotes;

/// <summary>
/// Answers "does this item satisfy a note's occupancy gate" with the game's own condition
/// records. The gate lives on auras whose action targets the occupying card
/// (TTargetCardOccupying) — the graph TTargetCardBase.FilterByConditions evaluates — so the
/// verdict includes non-tag gates like the E note's CooldownMax &gt; 0, not just the letter's
/// category tag. Ability triggers are event-keyed and carry no occupancy gate. Aura
/// prerequisites and ActiveIn are ignored: every note's base aura shares its gated variants'
/// occupancy conditions, so any-aura-passes is the fit signal. Extraction is pure over the
/// template graph and safe off-thread; Satisfies touches live card state — main thread only.
/// </summary>
internal static class MusicNoteFitEvaluator
{
    internal static IReadOnlyList<ITCardConditional> ExtractOccupyingConditions(
        TCardSocketEffect? template
    )
    {
        if (template?.Auras == null)
            return Array.Empty<ITCardConditional>();

        List<ITCardConditional>? conditions = null;
        try
        {
            foreach (var aura in template.Auras.Values)
            {
                if (
                    aura?.Action is TAuraActionCardBase
                    {
                        Target: TTargetCardOccupying { Conditions: ITCardConditional condition }
                    }
                )
                {
                    conditions ??= new List<ITCardConditional>(2);
                    conditions.Add(condition);
                }
            }
        }
        catch
        {
            // Template graphs are data-driven; keep whatever was collected before the surprise.
        }

        return (IReadOnlyList<ITCardConditional>?)conditions ?? Array.Empty<ITCardConditional>();
    }

    internal static bool Satisfies(
        IReadOnlyList<ITCardConditional>? conditions,
        ICard? card,
        IRun? run,
        ICard? noteEntity
    )
    {
        if (conditions == null || conditions.Count == 0 || card == null || run == null)
            return false;

        try
        {
            var context = new CardConditionalContext(
                run,
                new List<ICard> { card },
                noteEntity,
                null
            );
            foreach (var condition in conditions)
            {
                if (condition.IsSatisfiedBy(card, context))
                    return true;
            }
        }
        catch
        {
            // Condition types are data-driven; an unevaluable graph reads as "no fit".
        }

        return false;
    }
}
