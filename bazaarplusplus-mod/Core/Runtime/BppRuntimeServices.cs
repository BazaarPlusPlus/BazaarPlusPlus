#nullable enable
using System;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Paths;
using BazaarPlusPlus.Core.RunContext;
using BepInEx.Logging;

namespace BazaarPlusPlus.Core.Runtime;

internal sealed class BppRuntimeServices : IBppServices
{
    public BppRuntimeServices(
        IBppEventBus eventBus,
        IBppConfig config,
        IPathService paths,
        IRunContext runContext,
        IGameStateProbe gameStateProbe,
        ManualLogSource logger
    )
    {
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        RunContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
        GameStateProbe = gameStateProbe ?? throw new ArgumentNullException(nameof(gameStateProbe));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IBppEventBus EventBus { get; }
    public IBppConfig Config { get; }
    public IPathService Paths { get; }
    public IRunContext RunContext { get; }
    public IGameStateProbe GameStateProbe { get; }
    public ManualLogSource Logger { get; }
}
