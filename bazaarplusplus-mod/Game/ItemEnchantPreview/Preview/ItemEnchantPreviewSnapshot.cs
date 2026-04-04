using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.ItemEnchantPreview.Preview;

public sealed class ItemEnchantPreviewSnapshot
{
    public string InstanceId { get; set; } = string.Empty;

    public string TemplateId { get; set; } = string.Empty;

    public EInventorySection? Section { get; set; }

    public EEnchantmentType? CurrentEnchantment { get; set; }

    public EEnchantmentType PreviewEnchantment { get; set; }

    public Dictionary<ECardAttributeType, int> PreviewAttributes { get; set; } =
        new Dictionary<ECardAttributeType, int>();
}
