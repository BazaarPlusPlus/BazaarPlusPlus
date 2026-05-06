#nullable enable
namespace BazaarPlusPlus.Core.Paths;

internal interface IPathService
{
    string? RunLogDatabasePath { get; }

    string? CombatReplayDirectoryPath { get; }

    string? ScreenshotsDirectoryPath { get; }

    string? IdentityDirectoryPath { get; }
}
