#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

internal sealed class CollectionSourceOptionViewModel
{
    public string SourceKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public CollectionSourceKind Kind { get; init; }
    public Guid RepresentativeTemplateId { get; init; }
}
