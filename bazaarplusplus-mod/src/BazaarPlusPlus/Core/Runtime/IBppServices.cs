#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Storage.Paths;
using BepInEx.Logging;

namespace BazaarPlusPlus.Core.Runtime;

internal interface IBppServices
{
    IBppEventBus EventBus { get; }
    IBppConfig Config { get; }
    IPathProvider Paths { get; }
    IRunContext RunContext { get; }
    IGameStateProbe GameStateProbe { get; }
    IEncounterStateProbe EncounterState { get; }
    ManualLogSource Logger { get; }
}
