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
    public const string CompatibilityModeText = "compatibility mode";

    // modeText: how this launch runs the mod when that is not the usual way, such as
    // CompatibilityModeText; null shows nothing.
    public static string Build(
        string gameVersion,
        string pluginVersion,
        MainMenuUpdateStatus status = MainMenuUpdateStatus.None,
        string manualUpdateText = DesktopManualUpdateText,
        string restartFromAppText = RestartFromAppText,
        string? modeText = null
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
            _ => null,
        };
        var mode = string.IsNullOrWhiteSpace(modeText) ? string.Empty : $" | {modeText!.Trim()}";
        var suffix = statusText == null ? string.Empty : $" | {statusText}";
        return $" Version: {normalizedGameVersion} | BPP {pluginVersion.Trim()}{mode}{suffix} ";
    }
}
