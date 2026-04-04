#nullable enable
using System;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Paths;
using BazaarPlusPlus.Core.RunContext;

namespace BazaarPlusPlus.Core.Runtime;

internal sealed class BppRuntimeServices
{
    public BppRuntimeServices(
        IBppEventBus eventBus,
        IBppConfig config,
        IPathService paths,
        IMonsterCatalog monsterCatalog,
        IRunContext runContext,
        IGameStateProbe gameStateProbe
    )
    {
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        MonsterCatalog = monsterCatalog ?? throw new ArgumentNullException(nameof(monsterCatalog));
        RunContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
        GameStateProbe = gameStateProbe ?? throw new ArgumentNullException(nameof(gameStateProbe));
    }

    public IBppEventBus EventBus { get; }

    public IBppConfig Config { get; }

    public IPathService Paths { get; }

    public IMonsterCatalog MonsterCatalog { get; }

    public IRunContext RunContext { get; }

    public IGameStateProbe GameStateProbe { get; }
}
