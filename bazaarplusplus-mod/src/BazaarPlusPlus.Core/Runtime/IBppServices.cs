#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Core.Runtime;

public interface IBppServices
{
    IBppEventBus EventBus { get; }
    BppSettings Config { get; }
    IPathProvider Paths { get; }
    IRunContext RunContext { get; }
    IGameStateProbe GameStateProbe { get; }
    IEncounterStateProbe EncounterState { get; }
    IRunSnapshotProbe RunSnapshot { get; }
    IGameBuildInfo GameBuild { get; }
}
