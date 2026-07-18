#nullable enable
using BazaarPlusPlus.BazaarAgent;
using BazaarPlusPlus.GameInterop;
using BepInEx;

namespace BazaarPlusPlus.BazaarAgentHost;

/// <summary>
/// The BazaarAgent automation bridge as its own BepInEx plugin. It declares a hard dependency on
/// BazaarPlusPlus (so BazaarPlusPlus.Awake runs first and publishes the game-interop facade), reads
/// that facade through <see cref="BazaarAgentGameBridge"/>, and owns its own Unity lifecycle:
/// <see cref="Awake"/> builds the pure-core controller, <see cref="Update"/> pumps its tick, and
/// <see cref="OnDestroy"/> disposes it. BazaarPlusPlus does not know this plugin exists.
/// </summary>
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency(BppPluginMetadata.Guid)]
public sealed class BazaarAgentHostPlugin : BaseUnityPlugin
{
    public const string PluginGuid = MyPluginInfo.PLUGIN_GUID;
    public const string PluginName = MyPluginInfo.PLUGIN_NAME;
    public const string PluginVersion = MyPluginInfo.PLUGIN_VERSION;

    private BazaarAgentRuntimeController? _controller;
    private BazaarAgentBepInExLogger? _hostLogger;

    private void Awake()
    {
        var logger = new BazaarAgentBepInExLogger(Logger);
        _hostLogger = logger;
        var gameProbe = BazaarAgentGameBridge.Current;
        if (gameProbe is null)
        {
            logger.TryEmit(BazaarAgentLogEvents.HostInitializationFailed());
            return;
        }

        var options = new BazaarAgentBepInExOptions();
        var contextReader = new BazaarAgentGameContextReader(gameProbe, logger);
        var dispatcher = new BazaarAgentGameActionDispatcher();
        // Replay control (record/continue) is the only path that may exit ReplayState, and only
        // on an explicit POST /v1/replay/continue. No tick-driven replay auto-advance exists.
        var replaySink = new BazaarAgentGameReplayControlSink();

        _controller = new BazaarAgentRuntimeController(
            options,
            contextReader,
            dispatcher,
            replaySink,
            logger,
            new SystemBazaarAgentClock()
        );

        logger.TryEmit(BazaarAgentLogEvents.HostInitialized());
    }

    private void Update() => _controller?.Tick();

    private void OnDestroy()
    {
        _controller?.Dispose();
        _controller = null;
        _hostLogger?.Dispose();
        _hostLogger = null;
    }
}
