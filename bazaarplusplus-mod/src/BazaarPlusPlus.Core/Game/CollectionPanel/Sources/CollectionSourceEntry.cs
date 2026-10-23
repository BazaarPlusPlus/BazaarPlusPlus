#nullable enable
using System.Text;
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

public sealed class CollectionSourceEntry
{
    public CollectionSourceEntry(
        string sourceKey,
        CollectionSourceKind kind,
        string name,
        IReadOnlyList<CardHero> availableHeroes,
        string description,
        Guid portraitTemplateId,
        IReadOnlyList<Guid> sourceTemplateIds,
        IReadOnlyList<CollectionSourceOfferSegment> offerSegments,
        string group,
        int order,
        int groupDisplayIndex
    )
    {
        SourceKey = sourceKey;
        Kind = kind;
        Name = name;
        AvailableHeroes = availableHeroes;
        Description = description;
        PortraitTemplateId = portraitTemplateId;
        SourceTemplateIds = sourceTemplateIds;
        OfferSegments = offerSegments;
        Group = group;
        Order = order;
        GroupDisplayIndex = groupDisplayIndex;
        OfferRuleFingerprint = BuildOfferRuleFingerprint(offerSegments);
        SuppressDayGate = offerSegments.Count > 0 && offerSegments[0].Rule.StartingTier != null;
    }

    public string SourceKey { get; }

    public CollectionSourceKind Kind { get; }

    public string Name { get; }

    public IReadOnlyList<CardHero> AvailableHeroes { get; }

    public string Description { get; }

    public Guid PortraitTemplateId { get; }

    public IReadOnlyList<Guid> SourceTemplateIds { get; }

    public IReadOnlyList<CollectionSourceOfferSegment> OfferSegments { get; }

    public string OfferRuleFingerprint { get; }

    public bool SuppressDayGate { get; }

    public bool HasAllHeroesOfferSegment
    {
        get
        {
            foreach (var segment in OfferSegments)
            {
                if (segment.Rule.HeroMode == CollectionSourceHeroMode.AllHeroes)
                    return true;
            }
            return false;
        }
    }

    public string Group { get; }

    public int Order { get; }

    public int GroupDisplayIndex { get; }

    public bool AppliesToHero(CardHero hero) =>
        AvailableHeroes.Count == 0 || AvailableHeroes.Contains(hero);

    public bool IsVisibleForHero(CardHero hero)
    {
        if (!AppliesToHero(hero))
            return false;
        if (hero != CardHero.Common)
            return true;

        foreach (var segment in OfferSegments)
        {
            if (segment.Rule.HeroMode != CollectionSourceHeroMode.OtherHeroes)
                return true;
        }
        return false;
    }

    private static string BuildOfferRuleFingerprint(
        IReadOnlyList<CollectionSourceOfferSegment> segments
    )
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            if (builder.Length > 0)
                builder.Append(';');
            builder
                .Append(segment.Key)
                .Append(':')
                .Append(segment.Kind)
                .Append(':')
                .Append(segment.RarityLabel)
                .Append(':');
            AppendRule(builder, segment.Rule);
        }
        return builder.ToString();
    }

    private static void AppendRule(StringBuilder builder, CollectionSourceOfferRule rule)
    {
        builder
            .Append(rule.HeroMode)
            .Append('|')
            .Append(rule.Hero.HasValue ? rule.Hero.Value.ToString() : string.Empty)
            .Append('|')
            .Append(rule.StartingTier?.Mode.ToString() ?? string.Empty)
            .Append(':')
            .Append(rule.StartingTier?.Tier.ToString() ?? string.Empty)
            .Append('|')
            .AppendJoin(",", rule.SizesAny)
            .Append('|')
            .AppendJoin(",", rule.TagsAny)
            .Append('|')
            .AppendJoin(",", rule.TagsNone)
            .Append('|')
            .AppendJoin(",", rule.HiddenTagsAny)
            .Append('|')
            .Append(rule.EnchantableOnly ? "enchantable" : string.Empty)
            .Append('|')
            .AppendJoin(",", rule.EnchantmentTypesAny)
            .Append('|')
            .AppendJoin(",", rule.EnchantmentTagsAny)
            .Append('|')
            .AppendJoin(",", rule.EnchantmentHiddenTagsAny);
    }
}
