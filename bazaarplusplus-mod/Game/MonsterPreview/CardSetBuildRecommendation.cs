#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Game.ItemBoard;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class CardSetBuildRecommendation
{
    public string ModeLabel { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string SetSignature { get; set; } = string.Empty;

    public double GoldScore { get; set; }

    public int ResultIndex { get; set; }

    public int ResultCount { get; set; }

    public IReadOnlyList<ItemBoardItemSpec> Items { get; set; } = new List<ItemBoardItemSpec>();
}
