using BazaarPlusPlus.Core.GameState;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// Whether the player is in a run, read from the IL2CPP app state as the desktop's
/// <c>GameStateProbe</c> reads the Mono one. Proxies never satisfy <c>is</c>; type tests go
/// through <c>TryCast</c>.
/// </summary>
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
