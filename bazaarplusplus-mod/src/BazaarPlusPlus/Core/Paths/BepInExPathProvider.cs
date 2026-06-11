#nullable enable
using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Core.Paths;

internal sealed class BepInExPathProvider : IPathProvider
{
    public string? RunLogDatabasePath { get; private set; }

    public string? CombatReplayDirectoryPath { get; private set; }

    public string? ScreenshotsDirectoryPath { get; private set; }

    public string? CombatReplayVideoDirectoryPath { get; private set; }

    public string? CustomCardArtDirectoryPath { get; private set; }

    public string? PluginsDirectoryPath { get; private set; }

    public void Initialize()
    {
        RunLogDatabasePath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlusV4",
            PathConstants.RunLogDatabaseFileName
        );
        CombatReplayDirectoryPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlusV4",
            "CombatReplays"
        );
        ScreenshotsDirectoryPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlusV4",
            "Screenshots"
        );
        CombatReplayVideoDirectoryPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlusV4",
            "CombatReplayVideos"
        );
        CustomCardArtDirectoryPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlusV4",
            "CustomCardArt"
        );
        PluginsDirectoryPath = BepInEx.Paths.PluginPath;
    }
}
