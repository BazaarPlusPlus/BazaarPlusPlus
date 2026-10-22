using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Core.Runtime
{
    internal sealed class TestGameStateProbe : IGameStateProbe
    {
        public bool Result { get; set; }

        public bool ComputeIsInGameRun() => Result;
    }

    /// <summary>The run-state slice the status bar reads; no other service is reachable.</summary>
    internal sealed class TestServices : IBppServices
    {
        public static TestServices Instance { get; } = new TestServices();

        public IRunContext RunContext { get; } = new RunContextStore();

        public TestGameStateProbe GameStateProbe { get; } = new TestGameStateProbe();

        IGameStateProbe IBppServices.GameStateProbe => GameStateProbe;

        public IBppEventBus EventBus => throw new NotSupportedException();

        public BppSettings Config => throw new NotSupportedException();

        public IPathProvider Paths => throw new NotSupportedException();

        public IEncounterStateProbe EncounterState => throw new NotSupportedException();

        public IRunSnapshotProbe RunSnapshot => throw new NotSupportedException();

        public IGameBuildInfo GameBuild => throw new NotSupportedException();
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
