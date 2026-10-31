using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.Supporters;

namespace BazaarPlusPlus.Mobile.History;

/// <summary>
/// The host services the touch History reads, each a Core type the host's composition binds
/// (ADR-0025 decision 4).
/// </summary>
/// <param name="Dependencies">
/// History's coordinator dependencies: the history database, the replay service, the Mod API
/// session, the account link and the host's frame clock.
/// </param>
/// <param name="SavedReplays">The saved-replay runtime, or null while the host has none.</param>
/// <param name="LobbyActive">Whether the lobby is the active scene, where a replay returns.</param>
/// <param name="Templates">The card templates of the game data.</param>
/// <param name="Supporters">The supporter credit's rotation, or null where the host has none.</param>
internal sealed record TouchHistoryData(
    HistoryPanelDependencies Dependencies,
    Func<ISavedReplayRuntime?> SavedReplays,
    Func<bool> LobbyActive,
    IHistoryCardTemplates Templates,
    ISupporterRotation? Supporters
);
