using TheBazaar;

namespace BazaarPlusPlus.Core.GameState;

internal sealed class GameStateProbe : IGameStateProbe
{
    public bool ComputeIsInGameRun()
    {
        if (Data.IsInCombat)
            return true;

        var currentAppState = AppState.CurrentState;
        if (currentAppState is RunAppState)
            return !currentAppState.IsEndOfRunState();

        if (currentAppState is ReplayState)
            return true;

        return currentAppState is StartRunAppState;
    }
}
