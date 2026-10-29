#nullable enable
using BazaarPlusPlus.Host;
using BepInEx;
using BepInEx.Configuration;

namespace BazaarPlusPlus;

/// <summary>The desktop host: BepInEx loads this plugin and it runs <see cref="BppMonoHost"/>.</summary>
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private BppMonoHost? _host;

    protected virtual void Awake()
    {
        var paths = new BepInExPathProvider();
        paths.Initialize();
        _host = new BppMonoHost(
            this,
            new MonoHostOptions(
                new BepInExLogSink(Logger),
                () =>
                    new BepInExSettingsStore(
                        new ConfigFile(Path.Combine(Paths.ConfigPath, "BazaarPlusPlus.cfg"), true)
                    ),
                paths,
                Info.Location,
                selfUpdate: true
            )
        );
        _host.Start();
    }

    protected virtual void OnDestroy()
    {
        _host?.Stop();
    }
}
