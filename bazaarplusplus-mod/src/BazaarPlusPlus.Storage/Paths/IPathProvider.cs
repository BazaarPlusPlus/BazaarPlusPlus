#nullable enable
namespace BazaarPlusPlus.Storage.Paths;

public interface IPathProvider
{
    string? RunLogDatabasePath { get; }

    string? CombatReplayDirectoryPath { get; }

    string? ScreenshotsDirectoryPath { get; }

    string? CombatReplayVideoDirectoryPath { get; }

    string? CustomCardArtDirectoryPath { get; }

    string? PluginsDirectoryPath { get; }
}
