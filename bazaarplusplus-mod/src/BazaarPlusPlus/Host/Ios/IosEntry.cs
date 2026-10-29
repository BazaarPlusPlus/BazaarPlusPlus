#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Storage.Paths;
using UnityEngine;

namespace BazaarPlusPlus.Host.Ios;

/// <summary>
/// The iOS host. The App lists <see cref="Start"/> in the game's RuntimeInitializeOnLoads
/// (<c>bazaarplusplus-ios/runtime/helper/entrypoints.json</c>); BepInEx is not present there.
/// It starts <see cref="BppMonoHost"/> on a hidden, scene-independent GameObject, as the
/// BepInEx chainloader does for the desktop plugin.
/// </summary>
internal static class IosEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Start()
    {
        if (Application.platform != RuntimePlatform.IPhonePlayer)
            return;
        HostPlatformKey.UseIos();
        InterpreterMethodPatcher.Install();
        var host = new GameObject("BazaarPlusPlus") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<IosPlugin>();
    }
}

/// <summary>The component <see cref="BppMonoHost"/> mounts on in the iOS port.</summary>
internal sealed class IosPlugin : MonoBehaviour
{
    private BppMonoHost? _host;

    private void Awake()
    {
        var root = Application.persistentDataPath;
        _host = new BppMonoHost(
            this,
            new MonoHostOptions(
                new UnityLogSink(),
                () => IniSettingsStore.Open(Path.Combine(root, "BazaarPlusPlus.cfg")),
                new IosPathProvider(root),
                typeof(IosPlugin).Assembly.Location,
                selfUpdate: false
            )
        );
        _host.Start();
    }

    private void OnDestroy() => _host?.Stop();
}

/// <summary>The Data Root sits in the App's Documents directory (Unity's persistent data path).</summary>
internal sealed class IosPathProvider : IPathProvider
{
    public IosPathProvider(string documents) =>
        DataRootDirectoryPath = Path.Combine(documents, PathConstants.DataRootDirectoryName);

    public string? DataRootDirectoryPath { get; }
}

/// <summary>Writes finished <see cref="BppLog"/> lines to the Unity player log.</summary>
internal sealed class UnityLogSink : IBppLogSink
{
    public void Write(BppLogSeverity severity, string message)
    {
        switch (severity)
        {
            case BppLogSeverity.Warning:
                Debug.LogWarning("[BazaarPlusPlus] " + message);
                return;
            case BppLogSeverity.Error:
                Debug.LogError("[BazaarPlusPlus] " + message);
                return;
            default:
                Debug.Log("[BazaarPlusPlus] " + message);
                return;
        }
    }
}
