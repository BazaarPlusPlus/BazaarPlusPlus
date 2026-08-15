#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Players;
using BazaarPlusPlus.GameInterop.Cards;
using TheBazaar;

namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// Projects a PvE encounter's static monster template through the same detached-card path used
/// by combat summaries. This is deliberately the data source behind the game's native monster
/// board tooltip, rather than a prediction made from a later combat simulation.
/// </summary>
internal sealed class BazaarAgentCombatEncounterPreview : IBazaarAgentCombatEncounterPreview
{
    public BazaarAgentCombatEncounterPreviewSnapshot? Resolve(CombatEncounterCard encounter)
    {
        if (encounter is null)
            return null;

        try
        {
            var monster = encounter.GetMonsterTemplate();
            if (monster?.Player is not { } player)
                return null;

            return new BazaarAgentCombatEncounterPreviewSnapshot
            {
                Board = BuildItems(monster, player.Hand.Items),
                Skills = BuildSkills(monster, player.Skills),
                Health = ReadAttribute(player, EPlayerAttributeType.Health),
                MaxHealth = ReadAttribute(player, EPlayerAttributeType.HealthMax),
            };
        }
        catch
        {
            // Static data can be briefly unavailable during scene transitions. The caller keeps
            // the encounter selectable and falls back to its ordinary description.
            return null;
        }
    }

    private static IReadOnlyList<BazaarAgentBattleCardSnapshot> BuildItems(
        TMonster monster,
        IReadOnlyList<TCardInstanceItem> items
    )
    {
        var result = new List<BazaarAgentBattleCardSnapshot>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var card = CreateDetachedCard(monster, items[index], ECardType.Item, "item", index);
            if (card is not null)
                result.Add(card);
        }
        return result;
    }

    private static IReadOnlyList<BazaarAgentBattleCardSnapshot> BuildSkills(
        TMonster monster,
        IEnumerable<TCardInstanceSkill> skills
    )
    {
        var result = new List<BazaarAgentBattleCardSnapshot>();
        var index = 0;
        foreach (var skill in skills.OrderBy(skill => skill.InstanceId, StringComparer.Ordinal))
        {
            var card = CreateDetachedCard(monster, skill, ECardType.Skill, "skill", index++);
            if (card is not null)
                result.Add(card);
        }
        return result;
    }

    private static BazaarAgentBattleCardSnapshot? CreateDetachedCard(
        TMonster monster,
        TCardInstance source,
        ECardType type,
        string section,
        int index
    )
    {
        if (source.TemplateId == Guid.Empty)
            return null;

        var instanceId =
            $"bazaaragent-preview-{monster.Id:N}-{section}-{source.InstanceId ?? index.ToString()}";
        var card = DTOUtils.CreateCard(instanceId, source.TemplateId.ToString("D"), type);
        if (card.Template is not TCardBase template)
            return null;

        // Monster instances contain only their per-instance state. The native right-click board
        // preview first layers the matching tier's authored attributes and the enchantment over
        // that state, then copies static size/tags onto its detached client card. Mirror that
        // sequence; otherwise a raw DTO card reports Size=0 and renders authored values as zero.
        card.Attributes = new Dictionary<ECardAttributeType, int>(source.Attributes ?? []);
        card.Heroes = new HashSet<EHero>(template.Heroes);
        card.HiddenTags = new HashSet<EHiddenTag>(template.HiddenTags);
        card.Size = template.Size;
        card.Tags = new HashSet<ECardTag>(template.Tags);
        card.Tier = source.Tier;
        var attributes = TheBazaar.CardExtensions.BuildAttributeDictionaryForTier(
            card,
            template,
            source.Tier
        );

        if (source is TCardInstanceItem itemSource && card is ItemCard itemCard)
        {
            card.LeftSocketId = itemSource.SocketId;
            if (template is TCardItem itemTemplate)
                TheBazaar.CardExtensions.ApplyPreviewEnchantment(
                    itemTemplate,
                    itemCard,
                    attributes,
                    itemSource.EnchantmentType
                );
            else
                itemCard.Enchantment = itemSource.EnchantmentType;
        }
        card.Attributes = attributes;

        return new BazaarAgentBattleCardSnapshot
        {
            InstanceId = instanceId,
            TemplateId = card.TemplateId.ToString("D"),
            Type = card.Type.ToString(),
            DisplayName = CardDisplayNameResolver.Resolve(card.Template as TCardBase),
            Tier = card.Tier.ToString(),
            Size = card.Size.ToString(),
            Enchantment = (card as ItemCard)?.Enchantment?.ToString(),
            SocketId = card.LeftSocketId?.ToString(),
            Tags = card.Tags?.Select(tag => tag.ToString()).ToArray() ?? [],
            HiddenTags = card.HiddenTags?.Select(tag => tag.ToString()).ToArray() ?? [],
            Description = CardDescriptionResolver.Resolve(card),
            CooldownSeconds = ReadCooldownSeconds(card),
            Ammo = ReadAmmo(card),
            AmmoMax = ReadAmmoMax(card),
        };
    }

    private static int? ReadAttribute(TPlayer player, EPlayerAttributeType attribute) =>
        player.Attributes.TryGetValue(attribute, out var value) ? value : null;

    private static double? ReadCooldownSeconds(Card card)
    {
        var milliseconds = card.GetAttributeValue(ECardAttributeType.CooldownMax);
        return milliseconds is > 0 ? milliseconds.Value / 1000d : null;
    }

    private static int? ReadAmmoMax(Card card)
    {
        var ammoMax = card.GetAttributeValue(ECardAttributeType.AmmoMax);
        return ammoMax is > 0 ? ammoMax : null;
    }

    private static int? ReadAmmo(Card card) =>
        ReadAmmoMax(card) is null ? null : card.GetAttributeValue(ECardAttributeType.Ammo) ?? 0;
}
