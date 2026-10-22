#nullable enable
using BazaarPlusPlus.ManagedPayload;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace BazaarPlusPlus.Updater;

/// <summary>
/// The Desktop Host form: a BepInEx 5 preloader patcher in <c>BepInEx/patchers/</c>. The
/// preloader calls <see cref="Initialize"/> before it patches the game assemblies, and the
/// chainloader loads <c>BepInEx/plugins/</c> only after that, so no file this swaps is loaded
/// or locked yet. It patches nothing: the preloader discovers a patcher by the
/// <see cref="TargetDLLs"/> and <see cref="Patch"/> pair, and an empty target list means
/// <see cref="Patch"/> never runs.
/// </summary>
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
