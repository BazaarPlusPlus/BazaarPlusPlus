#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx;

namespace BazaarPlusPlus.Core.Paths;

internal static class CardJsonPathResolver
{
    public static string? GetCardsJsonPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return GetCardsJsonPath("mac", BepInEx.Paths.GameRootPath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetCardsJsonPath("windows", BepInEx.Paths.GameRootPath);

        return null;
    }

    internal static string? GetCardsJsonPath(string platform)
    {
        return GetCardsJsonPath(platform, null);
    }

    internal static string? GetCardsJsonPath(string platform, string? gameRootPath)
    {
        if (string.IsNullOrWhiteSpace(gameRootPath))
            return null;

        if (string.Equals(platform, "mac", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(
                gameRootPath,
                "TheBazaar.app",
                "Contents",
                "Resources",
                "Data",
                "StreamingAssets",
                "cards.json"
            );
        }

        if (string.Equals(platform, "windows", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(gameRootPath, "TheBazaar_Data", "StreamingAssets", "cards.json");

        return null;
    }
}
