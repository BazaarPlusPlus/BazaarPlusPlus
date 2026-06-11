#nullable enable
using System;
using System.Text.RegularExpressions;

namespace BazaarPlusPlus.Game.Lobby;

internal static class MainMenuVersionComparer
{
    private static readonly Regex VersionPattern = new(
        @"(?<!\d)(\d+)\.(\d+)\.(\d+)",
        RegexOptions.CultureInvariant
    );

    public static bool IsUpdateAvailable(string currentVersion, string latestVersion)
    {
        if (!TryParseVersion(currentVersion, out var current))
            return false;
        if (!TryParseVersion(latestVersion, out var latest))
            return false;

        return current.CompareTo(latest) < 0;
    }

    internal static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var match = VersionPattern.Match(value.Trim());
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups[1].Value, out var major))
            return false;
        if (!int.TryParse(match.Groups[2].Value, out var minor))
            return false;
        if (!int.TryParse(match.Groups[3].Value, out var patch))
            return false;

        version = new Version(major, minor, patch);
        return true;
    }
}
