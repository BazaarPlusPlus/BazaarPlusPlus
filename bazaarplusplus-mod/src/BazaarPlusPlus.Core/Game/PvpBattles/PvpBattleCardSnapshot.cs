#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.PvpBattles;

// The game's unnamed size 0 can appear on opening-snapshot skills before any size update.
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
