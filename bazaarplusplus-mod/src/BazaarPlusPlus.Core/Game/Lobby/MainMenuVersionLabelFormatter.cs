#nullable enable
namespace BazaarPlusPlus.Game.Lobby;

public enum MainMenuUpdateStatus
{
    None,

    Updating,

    RestartToUpdate,

    ManualUpdate,

    // The Android App alone stages updates on that host, so restart the game from the App to install them.
    RestartFromApp,
}

public static class MainMenuVersionLabelFormatter
{
    public const string UpdatingText = "updating";
    public const string RestartToUpdateText = "restart to update";
    public const string RestartFromAppText = "restart from the BPP App";
    public const string DesktopManualUpdateText = "update via installer";
    public const string AndroidManualUpdateText = "install the new BPP App";
    public const string GameNotAdaptedText = "not adapted to this game version";
    public const string GameOlderText = "update the game";

    public static string Build(
        string gameVersion,
        string pluginVersion,
        MainMenuUpdateStatus status = MainMenuUpdateStatus.None,
        string manualUpdateText = DesktopManualUpdateText,
        string restartFromAppText = RestartFromAppText,
        string? adaptationText = null
    )
    {
        var normalizedGameVersion = string.IsNullOrWhiteSpace(gameVersion)
            ? "unknown"
            : gameVersion.Trim();
        if (string.IsNullOrWhiteSpace(pluginVersion))
            return $" Version: {normalizedGameVersion} ";

        var statusText = status switch
        {
            MainMenuUpdateStatus.Updating => UpdatingText,
            MainMenuUpdateStatus.RestartToUpdate => RestartToUpdateText,
            MainMenuUpdateStatus.ManualUpdate => manualUpdateText,
            MainMenuUpdateStatus.RestartFromApp => restartFromAppText,
            _ => string.IsNullOrWhiteSpace(adaptationText) ? null : adaptationText,
        };
        var suffix = statusText == null ? string.Empty : $" | {statusText}";
        return $" Version: {normalizedGameVersion} | BPP {pluginVersion.Trim()}{suffix} ";
    }
}
