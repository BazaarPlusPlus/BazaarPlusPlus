#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>
/// History's card templates on the Mono host: the game's static data, read on each lookup
/// (<see cref="HistoryBattlePreviewProjection.HasCardTemplate"/>), so nothing is built ahead.
/// </summary>
internal sealed class DesktopHistoryCardTemplates : IHistoryCardTemplates
{
    public static readonly DesktopHistoryCardTemplates Instance = new();

    private DesktopHistoryCardTemplates() { }

    public void EnsureBuilt() { }

    public bool Contains(Guid templateId) =>
        HistoryBattlePreviewProjection.HasCardTemplate(templateId);
}
