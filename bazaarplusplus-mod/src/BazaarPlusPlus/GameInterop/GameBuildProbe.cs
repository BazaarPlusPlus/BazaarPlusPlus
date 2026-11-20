#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Core.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop;

internal static class GameBuildProbe
{
    public static GameBuildInfo Resolve()
    {
        var rawVersion = string.Empty;
        try
        {
            rawVersion = Application.version ?? string.Empty;
        }
        catch { }

        bool? hasServerOption = null;
        try
        {
            hasServerOption = AccessTools.Inner(typeof(TheBazaar.Config), "ServerOption") != null;
        }
        catch { }

        return GameBuildInfoResolver.Resolve(rawVersion, hasServerOption);
    }

    // Read on the Unity main thread because this accesses Application.persistentDataPath.
    public static string? GameDataCacheDirectory()
    {
        try
        {
            return TheBazaar.DataManagement.DataManifestActions.GetCachePath();
        }
        catch
        {
            return null;
        }
    }
}
