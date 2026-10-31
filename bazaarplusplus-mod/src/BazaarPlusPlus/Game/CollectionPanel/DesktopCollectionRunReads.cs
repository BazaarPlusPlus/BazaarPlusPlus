#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel;

/// <summary>The touch Collection's reads of the run on the Mono host, over its run services and game data.</summary>
internal sealed class DesktopCollectionRunReads(IBppServices services) : ICollectionRunReads
{
    private readonly IBppServices _services =
        services ?? throw new ArgumentNullException(nameof(services));

    public bool IsInRun()
    {
        try
        {
            return _services.RunContext.IsInGameRun
                || _services.GameStateProbe.ComputeIsInGameRun();
        }
        catch (Exception)
        {
            return _services.RunContext.IsInGameRun;
        }
    }

    public int? Day()
    {
        try
        {
            return (int?)TheBazaar.Data.Run?.Day;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public CardHero? Hero()
    {
        try
        {
            var player = TheBazaar.Data.Run?.Player;
            if (
                player != null
                && DesktopCardFacets.Hero.TryToFacet(player.Hero, out var runHero)
                && CollectionPanelSelectionState.IsConcreteHero(runHero)
            )
                return runHero;
            return
                DesktopCardFacets.Hero.TryToFacet(TheBazaar.Data.SelectedHero, out var selected)
                && CollectionPanelSelectionState.IsConcreteHero(selected)
                ? selected
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
