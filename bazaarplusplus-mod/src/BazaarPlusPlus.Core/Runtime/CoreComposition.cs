#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Core.Runtime;

// Stop features in reverse startup order so dependencies stay available during teardown.
public sealed class CoreComposition
{
    private readonly BppFeatureRegistry _features = new();

    public CoreComposition(
        IBppSettingsStore settingsStore,
        IPathProvider paths,
        IGameStateProbe gameStateProbe,
        IEncounterStateProbe encounterState,
        IRunSnapshotProbe runSnapshot,
        IGameBuildInfo gameBuild
    )
    {
        if (settingsStore == null)
            throw new ArgumentNullException(nameof(settingsStore));

        Services = new BppRuntimeServices(
            new InMemoryBppEventBus(),
            new BppSettings(settingsStore),
            paths,
            new RunContextStore(),
            gameStateProbe,
            encounterState,
            runSnapshot,
            gameBuild
        );
    }

    public IBppServices Services { get; }

    public void Register(IBppFeature feature) => _features.Register(feature);

    public void Start() => _features.Start();

    public void Stop() => _features.Stop();
}
