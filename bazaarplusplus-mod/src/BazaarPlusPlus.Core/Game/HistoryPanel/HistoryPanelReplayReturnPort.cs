#nullable enable
using BazaarPlusPlus.Game.CombatReplay;

namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>
/// The host side of History's return from a replay it started. <see cref="Hide"/> hides the panel
/// for the replay just accepted; <see cref="Show"/> requests the panel open, and that open calls
/// <see cref="HistoryPanelCoordinator.OnPanelShown"/>. The replay is over once
/// <see cref="SavedReplays"/> reports no saved replay session (a null runtime keeps the return
/// waiting) and <see cref="LobbyActive"/> says the host's lobby is the active scene.
/// </summary>
public sealed record HistoryPanelReplayReturnPort(
    Action Hide,
    Action Show,
    Func<ISavedReplayRuntime?> SavedReplays,
    Func<bool> LobbyActive
);
