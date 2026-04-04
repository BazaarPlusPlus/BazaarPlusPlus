#nullable enable
namespace BazaarPlusPlus.Core.Paths;

internal interface IPathService
{
    string? CardsJsonPath { get; }

    string? RunLogDatabasePath { get; }

    string? CombatReplayDirectoryPath { get; }

    string? RunUploadInstallIdentityPath { get; }

    string? RunUploadClientStatePath { get; }

    string? RunUploadPrivateKeyPath { get; }
}
