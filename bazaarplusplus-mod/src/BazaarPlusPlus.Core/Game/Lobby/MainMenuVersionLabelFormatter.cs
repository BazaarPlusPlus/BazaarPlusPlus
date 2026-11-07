#nullable enable
namespace BazaarPlusPlus.Game.Lobby;

/// <summary>Where the running copy stands against the published release.</summary>
public enum MainMenuUpdateStatus
{
    None,

    /// <summary>The newer Managed Payload is downloading.</summary>
    Updating,

    /// <summary>The newer Managed Payload is staged; the next start swaps it in.</summary>
    RestartToUpdate,

    /// <summary>A newer release this copy cannot take by itself.</summary>
    ManualUpdate,

    /// <summary>
    /// A newer Managed Payload this copy can take, which only the Android App stages: starting
    /// the game again from the App installs it.
    /// </summary>
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
            // The self-update status comes first; the game's adaptation only fills an empty suffix.
            _ => string.IsNullOrWhiteSpace(adaptationText) ? null : adaptationText,
        };
        var suffix = statusText == null ? string.Empty : $" | {statusText}";
        return $" Version: {normalizedGameVersion} | BPP {pluginVersion.Trim()}{suffix} ";
    }
}
