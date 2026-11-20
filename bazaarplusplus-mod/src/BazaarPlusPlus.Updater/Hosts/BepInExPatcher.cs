#nullable enable
using BazaarPlusPlus.ManagedPayload;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace BazaarPlusPlus.Updater;

// BepInEx 5 runs Initialize before the chainloader can lock plugin files. It discovers
// the patcher from TargetDLLs and Patch even when there are no assemblies to patch.
public static class BepInExPatcher
{
    public static IEnumerable<string> TargetDLLs { get; } = Array.Empty<string>();

    public static void Initialize()
    {
        var source = Logger.CreateLogSource("BazaarPlusPlus.Updater");
        var log = new UpdaterLog(
            message => source.LogInfo(message),
            message => source.LogWarning(message),
            message => source.LogError(message)
        );
        UpdaterRun.Run(
            Paths.GameRootPath,
            ManagedPayloadHost.BepInEx,
            UpdaterRun.DesktopPlatformKey(),
            log
        );
        Logger.Sources.Remove(source);
    }

    public static void Patch(AssemblyDefinition assembly) { }
}
