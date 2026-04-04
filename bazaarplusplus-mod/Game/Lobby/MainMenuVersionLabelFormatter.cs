#nullable enable
namespace BazaarPlusPlus.Game.Lobby;

internal static class MainMenuVersionLabelFormatter
{
    public static string Build(string gameVersion, string pluginVersion)
    {
        var normalizedGameVersion = string.IsNullOrWhiteSpace(gameVersion)
            ? "unknown"
            : gameVersion.Trim();
        if (string.IsNullOrWhiteSpace(pluginVersion))
            return $" Version: {normalizedGameVersion} ";

        return $" Version: {normalizedGameVersion} | BPP {pluginVersion.Trim()} ";
    }
}
