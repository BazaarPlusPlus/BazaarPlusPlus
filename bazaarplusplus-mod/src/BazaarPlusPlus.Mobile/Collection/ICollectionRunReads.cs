using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Mobile.Collection;

// Read on the main thread. A failed read yields no Run, day or hero.
internal interface ICollectionRunReads
{
    bool IsInRun();

    // The game retains its last Run in the main menu, so read its day only during an active Run.
    int? Day();

    CardHero? Hero();
}
