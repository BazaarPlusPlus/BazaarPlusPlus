#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>
/// The card templates the host's game data holds, which History asks before it shows a battle
/// snapshot's card (ADR-0025 decision 4: a host service the Mobile UI reaches through Core). Each
/// host's composition binds it over its own read of the game's static data.
/// </summary>
public interface IHistoryCardTemplates
{
    /// <summary>
    /// Main thread, when the panel opens: readies the templates before a Ghost replay's download
    /// reads them on a pool thread. A host that reads the game data on each lookup does nothing.
    /// </summary>
    void EnsureBuilt();

    /// <summary>Whether the game data holds the template.</summary>
    bool Contains(Guid templateId);
}
