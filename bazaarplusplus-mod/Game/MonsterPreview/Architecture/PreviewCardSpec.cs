#pragma warning disable CS0436
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class PreviewCardSpec
{
    public string TemplateId { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;

    public int Tier { get; set; }

    public int Size { get; set; } = 1;

    public string Enchant { get; set; } = "None";

    public Dictionary<int, int> Attributes { get; set; } = new Dictionary<int, int>();
}
