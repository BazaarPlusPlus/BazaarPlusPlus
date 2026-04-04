using System.Collections.Generic;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.ItemEnchantPreview.Preview;

public static class ItemEnchantPreviewSnapshotFactory
{
    public static ItemEnchantPreviewSnapshot Create(
        ItemCard itemCard,
        EEnchantmentType previewEnchantment,
        TEnchantment previewTemplate
    )
    {
        var previewAttributes = new Dictionary<ECardAttributeType, int>(itemCard.Attributes);

        if (previewTemplate?.Attributes != null)
        {
            foreach (var attribute in previewTemplate.Attributes)
                previewAttributes[attribute.Key] = attribute.Value;
        }

        return new ItemEnchantPreviewSnapshot
        {
            InstanceId = itemCard.InstanceId.ToString(),
            TemplateId = itemCard.TemplateId.ToString(),
            Section = itemCard.Section,
            CurrentEnchantment = itemCard.Enchantment,
            PreviewEnchantment = previewEnchantment,
            PreviewAttributes = previewAttributes,
        };
    }
}
