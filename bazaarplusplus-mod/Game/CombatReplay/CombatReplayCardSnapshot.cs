#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayCardSnapshot
{
    public string InstanceId { get; set; } = string.Empty;

    public string TemplateId { get; set; } = string.Empty;

    public ECardType Type { get; set; }

    public ECardSize Size { get; set; }

    public EInventorySection? Section { get; set; }

    public EContainerSocketId? Socket { get; set; }

    public string? Name { get; set; }

    public string? Tier { get; set; }

    public string? Enchant { get; set; }

    public IList<string> Tags { get; set; } = new List<string>();

    public IDictionary<string, int> Attributes { get; set; } = new Dictionary<string, int>();

    public CombatReplayCardSnapshot Clone()
    {
        return new CombatReplayCardSnapshot
        {
            InstanceId = InstanceId,
            TemplateId = TemplateId,
            Type = Type,
            Size = Size,
            Section = Section,
            Socket = Socket,
            Name = Name,
            Tier = Tier,
            Enchant = Enchant,
            Tags = new List<string>(Tags),
            Attributes = new Dictionary<string, int>(Attributes),
        };
    }
}
