using BazaarPlusPlus.Core.GameState;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

// IL2CPP proxies require TryCast for game type tests.
internal sealed class AndroidGameStateProbe : IGameStateProbe
{
    public bool ComputeIsInGameRun()
    {
        if (Data.IsInCombat)
            return true;

        var current = AppState.CurrentState;
        if (current == null)
            return false;
        if (current.TryCast<RunAppState>() != null)
            return !DataExtensions.IsEndOfRunState(current);
        if (current.TryCast<ReplayState>() != null)
            return true;
        return current.TryCast<StartRunAppState>() != null;
    }
}
