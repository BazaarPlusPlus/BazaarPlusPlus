using System.Collections.Generic;
using BazaarGameClient.Domain.Models.Cards;

namespace BazaarPlusPlus.Game.ItemEnchantPreview.Preview;

public static class ItemEnchantPreviewCardCloneFactory
{
    public static ItemCard Create(ItemCard source, ItemEnchantPreviewSnapshot snapshot)
    {
        return new ItemCard
        {
            InstanceId = source.InstanceId,
            TemplateId = source.TemplateId,
            Attributes = new Dictionary<BazaarGameShared.Domain.Core.Types.ECardAttributeType, int>(
                snapshot.PreviewAttributes
            ),
            Heroes = new HashSet<BazaarGameShared.Domain.Core.Types.EHero>(source.Heroes),
            HiddenTags = new HashSet<BazaarGameShared.Domain.Core.Types.EHiddenTag>(
                source.HiddenTags
            ),
            Size = source.Size,
            Tags = new HashSet<BazaarGameShared.Domain.Core.Types.ECardTag>(source.Tags),
            Tier = source.Tier,
            Type = source.Type,
            Owner = source.Owner,
            LeftSocketId = source.LeftSocketId,
            Section = source.Section,
            State = source.State,
            Template = source.Template,
            Enchantment = snapshot.PreviewEnchantment,
        };
    }
}
