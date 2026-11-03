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

    /// <summary>The platform token of the game version string on the iOS port.</summary>
    public const string IosPlatformToken = "bppios";

    private const string MacPlatformToken = "macos";

    /// <summary>
    /// The game version the iOS port shows. The App runs the game's macOS build, so its version
    /// string carries the macOS platform token (<c>1.0.12651-prod-macos-arm64-…</c>); the label
    /// names the port instead. Display only: the raw version still identifies the build.
    /// </summary>
    public static string IosGameVersion(string gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
            return gameVersion;
        var tokens = gameVersion.Split('-');
        for (var i = 0; i < tokens.Length; i++)
        {
            if (string.Equals(tokens[i], MacPlatformToken, StringComparison.OrdinalIgnoreCase))
                tokens[i] = IosPlatformToken;
        }
        return string.Join("-", tokens);
    }

    public static string Build(
        string gameVersion,
        string pluginVersion,
        MainMenuUpdateStatus status = MainMenuUpdateStatus.None,
        string manualUpdateText = DesktopManualUpdateText,
        string restartFromAppText = RestartFromAppText
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
        var suffix = statusText == null ? string.Empty : $" | {statusText}";
        return $" Version: {normalizedGameVersion} | BPP {pluginVersion.Trim()}{suffix} ";
    }
}
