#pragma warning disable CS0436
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class InMemoryPreviewDataSource : IPreviewDataSource
{
    private IReadOnlyList<PreviewCardSpec> _itemCards = new List<PreviewCardSpec>();
    private IReadOnlyList<PreviewCardSpec> _skillCards = new List<PreviewCardSpec>();
    private IReadOnlyDictionary<string, string> _metadata = new Dictionary<string, string>();
    private string _title = string.Empty;

    public void SetCards(
        IReadOnlyList<PreviewCardSpec> itemCards,
        IReadOnlyList<PreviewCardSpec> skillCards
    )
    {
        _itemCards = CloneCards(itemCards);
        _skillCards = CloneCards(skillCards);
    }

    public void SetMetadata(string title, IReadOnlyDictionary<string, string> metadata = null)
    {
        _title = title ?? string.Empty;
        _metadata = metadata ?? new Dictionary<string, string>();
    }

    public bool TryBuild(out PreviewBoardModel model)
    {
        model = new PreviewBoardModel
        {
            Title = _title,
            ItemCards = CloneCards(_itemCards),
            SkillCards = CloneCards(_skillCards),
            Metadata = new Dictionary<string, string>(_metadata),
        };
        model.Signature = PreviewBoardSignature.Build(model);
        return true;
    }

    private static IReadOnlyList<PreviewCardSpec> CloneCards(IReadOnlyList<PreviewCardSpec> cards)
    {
        return cards
                ?.Select(card => new PreviewCardSpec
                {
                    TemplateId = card.TemplateId,
                    Tier = card.Tier,
                    SourceName = card.SourceName,
                    Enchant = card.Enchant,
                    Size = card.Size,
                    Attributes =
                        card.Attributes != null
                            ? new Dictionary<int, int>(card.Attributes)
                            : new Dictionary<int, int>(),
                })
                .ToList()
            ?? new List<PreviewCardSpec>();
    }
}
