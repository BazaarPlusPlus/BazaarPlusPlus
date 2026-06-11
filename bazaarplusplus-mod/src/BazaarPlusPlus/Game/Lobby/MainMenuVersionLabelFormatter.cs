#nullable enable
namespace BazaarPlusPlus.Game.Lobby;

internal static class MainMenuVersionLabelFormatter
{
    public const string UpdateAvailableText = "update available";

    public static string Build(
        string gameVersion,
        string pluginVersion,
        bool updateAvailable = false
    )
    {
        var normalizedGameVersion = string.IsNullOrWhiteSpace(gameVersion)
            ? "unknown"
            : gameVersion.Trim();
        if (string.IsNullOrWhiteSpace(pluginVersion))
            return $" Version: {normalizedGameVersion} ";

        var updateText = updateAvailable ? $" | {UpdateAvailableText}" : string.Empty;
        return $" Version: {normalizedGameVersion} | BPP {pluginVersion.Trim()}{updateText} ";
    }
}
