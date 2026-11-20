#nullable enable
using BazaarPlusPlus.Game.CombatReplay;

namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>Show must call OnPanelShown to consume the pending return. A missing replay runtime leaves it waiting.</summary>
public sealed record HistoryPanelReplayReturnPort(
    Action Hide,
    Action Show,
    Func<ISavedReplayRuntime?> SavedReplays,
    Func<bool> LobbyActive
);
