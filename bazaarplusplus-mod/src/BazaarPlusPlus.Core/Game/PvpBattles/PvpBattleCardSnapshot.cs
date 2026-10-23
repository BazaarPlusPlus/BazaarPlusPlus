#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.PvpBattles;

/// <summary>
/// One captured card on BPP card facets (ADR-0015). <see cref="Size"/> is null when the game
/// reported no size for the card (its unnamed value 0, as an opening-snapshot skill without an
/// update has); <see cref="Section"/> and <see cref="Socket"/> are null when the card had no
/// placement or its placement maps to no facet.
/// </summary>
public sealed class PvpBattleCardSnapshot
{
    public string InstanceId { get; set; } = string.Empty;

    public string TemplateId { get; set; } = string.Empty;

    public CardType Type { get; set; }

    public CardSize? Size { get; set; }

    public CardSection? Section { get; set; }

    public CardSocket? Socket { get; set; }

    public string? Name { get; set; }

    public string? Tier { get; set; }

    public string? Enchant { get; set; }

    public IList<string> Tags { get; set; } = new List<string>();

    public IDictionary<string, int> Attributes { get; set; } = new Dictionary<string, int>();

    public PvpBattleCardSnapshot Clone()
    {
        return new PvpBattleCardSnapshot
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
