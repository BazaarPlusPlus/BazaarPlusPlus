#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Core.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop;

/// <summary>Reads the desktop client's build signals for <see cref="GameBuildInfoResolver"/>.</summary>
internal static class GameBuildProbe
{
    public static GameBuildInfo Resolve()
    {
        var rawVersion = string.Empty;
        try
        {
            rawVersion = Application.version ?? string.Empty;
        }
        catch
        {
            // Fall through to Unknown below.
        }

        bool? hasServerOption = null;
        try
        {
            hasServerOption = AccessTools.Inner(typeof(TheBazaar.Config), "ServerOption") != null;
        }
        catch
        {
            // The version signal decides alone when the probe is unreadable.
        }

        return GameBuildInfoResolver.Resolve(rawVersion, hasServerOption);
    }
}
