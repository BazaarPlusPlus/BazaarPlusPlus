#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Core.Runtime;

/// <summary>
/// The host-independent half of the composition root: the event bus, settings, run context, the
/// services every feature receives, and the feature lifecycle. A host composition supplies the
/// ports (settings store, paths, game-state probes, game build), then registers its features in
/// start order; <see cref="Stop"/> runs them in reverse.
/// </summary>
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
