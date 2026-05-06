using System.Collections.Generic;

namespace BazaarPlusPlus.Game.PreviewSurface;

internal sealed class PreviewBoardModel
{
    public IReadOnlyList<PreviewCardSpec> ItemCards { get; set; } = new List<PreviewCardSpec>();

    public IReadOnlyList<PreviewCardSpec> SkillCards { get; set; } = new List<PreviewCardSpec>();

    public string Title { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;

    public IReadOnlyDictionary<string, string> Metadata { get; set; } =
        new Dictionary<string, string>();
}
