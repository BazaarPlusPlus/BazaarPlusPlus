using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Core.Runtime
{
    internal interface IRunContext
    {
        bool IsInGameRun { get; set; }
    }

    internal interface IBppServices
    {
        IRunContext RunContext { get; }
        IGameStateProbe GameStateProbe { get; }
    }

    internal sealed class TestRunContext : IRunContext
    {
        public bool IsInGameRun { get; set; }
    }

    internal sealed class TestGameStateProbe : IGameStateProbe
    {
        public bool Result { get; set; }

        public bool ComputeIsInGameRun() => Result;
    }

    internal sealed class TestServices : IBppServices
    {
        public static TestServices Instance { get; } = new TestServices();

        public IRunContext RunContext { get; } = new TestRunContext();

        public TestGameStateProbe GameStateProbe { get; } = new TestGameStateProbe();

        IGameStateProbe IBppServices.GameStateProbe => GameStateProbe;
    }
}

namespace BazaarPlusPlus.Game.CombatStatusBar
{
    internal sealed partial class CombatStatusBar
    {
        private static IBppServices? _services;

        static CombatStatusBar()
        {
            _services = TestServices.Instance;
        }
    }
}

// The game declares both runtime types in the global namespace; keep the shim's compile surface
// identical so linked production sources resolve names the same way as the real assemblies.
public sealed class GameServiceManager
{
    public bool GamePaused { get; private set; }

    public void PauseOrUnpauseGame(bool paused)
    {
        GamePaused = paused;
    }
}

public sealed class Singleton<T>
    where T : class
{
    public static T? Instance { get; set; }
}
