using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel;

namespace BazaarPlusPlus.Mobile.History;

internal sealed record TouchHistoryData(
    HistoryPanelDependencies Dependencies,
    Func<ISavedReplayRuntime?> SavedReplays,
    Func<bool> LobbyActive,
    IHistoryCardTemplates Templates,
    ISupporterRotation? Supporters
);
