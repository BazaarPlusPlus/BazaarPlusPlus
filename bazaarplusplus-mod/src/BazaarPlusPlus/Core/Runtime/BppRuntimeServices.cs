#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Storage.Paths;
using BepInEx.Logging;

namespace BazaarPlusPlus.Core.Runtime;

internal sealed class BppRuntimeServices : IBppServices
{
    public BppRuntimeServices(
        IBppEventBus eventBus,
        IBppConfig config,
        IPathProvider paths,
        IRunContext runContext,
        IGameStateProbe gameStateProbe,
        IEncounterStateProbe encounterState,
        IRunSnapshotProbe runSnapshot,
        IGameBuildInfo gameBuild,
        ManualLogSource logger
    )
    {
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        RunContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
        GameStateProbe = gameStateProbe ?? throw new ArgumentNullException(nameof(gameStateProbe));
        EncounterState = encounterState ?? throw new ArgumentNullException(nameof(encounterState));
        RunSnapshot = runSnapshot ?? throw new ArgumentNullException(nameof(runSnapshot));
        GameBuild = gameBuild ?? throw new ArgumentNullException(nameof(gameBuild));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IBppEventBus EventBus { get; }
    public IBppConfig Config { get; }
    public IPathProvider Paths { get; }
    public IRunContext RunContext { get; }
    public IGameStateProbe GameStateProbe { get; }
    public IEncounterStateProbe EncounterState { get; }
    public IRunSnapshotProbe RunSnapshot { get; }
    public IGameBuildInfo GameBuild { get; }
    public ManualLogSource Logger { get; }
}
