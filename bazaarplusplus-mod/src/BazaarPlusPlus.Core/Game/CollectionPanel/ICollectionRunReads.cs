#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel;

/// <summary>
/// What the touch Collection reads of the game's run when it opens (ADR-0025 decision 4). Each
/// host binds it over its own game types; a read that fails reads as no run, no day or no hero.
/// </summary>
public interface ICollectionRunReads
{
    /// <summary>Main thread. Whether a run is on.</summary>
    bool IsInRun();

    /// <summary>
    /// Main thread, in a run only: the game keeps the last run's object in the main menu, so its
    /// day there is stale.
    /// </summary>
    int? Day();

    /// <summary>Main thread. The run's hero, else the lobby's selected one; null when neither is concrete.</summary>
    CardHero? Hero();
}
