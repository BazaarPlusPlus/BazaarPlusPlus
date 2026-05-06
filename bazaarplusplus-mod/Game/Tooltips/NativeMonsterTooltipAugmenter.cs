#nullable enable
using System;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Combat;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Players;
using BazaarPlusPlus.Core.Runtime;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class NativeMonsterTooltipAugmenter
{
    internal static bool TryAugment(
        Card card,
        CardTooltipData tooltipData,
        out CardTooltipData? augmentedTooltipData,
        string logContext
    )
    {
        augmentedTooltipData = null;
        if (
            card == null
            || tooltipData == null
            || CardTooltipDataFactory.GetMonster(tooltipData) != null
        )
            return false;

        if (!TryResolveMonster(card, out var monster, out var reason))
        {
            BppLog.Debug(
                "NativeMonsterTooltipAugmenter",
                $"Skipped augment context={logContext} card={card.Template?.InternalName ?? "-"} templateId={card.TemplateId} reason={reason}"
            );
            return false;
        }

        augmentedTooltipData = CardTooltipDataFactory.Create(card, tooltipData, monster);
        BppLog.Info(
            "NativeMonsterTooltipAugmenter",
            $"Augmented tooltip context={logContext} card={card.Template?.InternalName ?? "-"} templateId={card.TemplateId} monster={monster?.InternalName ?? monster?.Id.ToString() ?? "-"}"
        );
        return true;
    }

    internal static bool TryResolveMonster(Card card, out TMonster? monster, out string reason)
    {
        monster = null;

        if (card == null)
        {
            reason = "card_null";
            return false;
        }

        if (card.Type != ECardType.CombatEncounter)
        {
            reason = "card_type_not_combat_encounter";
            return false;
        }

        if (card.Template is not TCardEncounterCombat encounterTemplate)
        {
            reason = "template_not_combat_encounter";
            return false;
        }

        if (encounterTemplate.CombatantType is not TCombatantMonster combatant)
        {
            reason = "combatant_not_monster";
            return false;
        }

        if (combatant.MonsterTemplateId == Guid.Empty)
        {
            reason = "monster_template_id_empty";
            return false;
        }

        var staticData = BppStaticDataAccess.TryGet();
        if (staticData == null)
        {
            reason = "static_data_unavailable";
            return false;
        }

        var getMonsterByIdMethod =
            AccessTools.Method(staticData.GetType(), "GetMonsterById")
            ?? staticData.GetType().GetMethod("GetMonsterById", new[] { typeof(Guid) });
        monster =
            getMonsterByIdMethod?.Invoke(staticData, new object[] { combatant.MonsterTemplateId })
            as TMonster;
        if (monster == null)
        {
            reason = $"monster_template_missing:{combatant.MonsterTemplateId}";
            return false;
        }

        reason = "resolved";
        return true;
    }
}
