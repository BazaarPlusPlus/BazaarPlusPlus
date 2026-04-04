#nullable enable
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;

namespace BazaarPlusPlus.Core.Paths;

internal sealed class BppPathService : IPathService
{
    public string? CardsJsonPath { get; private set; }

    public string? RunLogDatabasePath { get; private set; }

    public string? CombatReplayDirectoryPath { get; private set; }

    public string? RunUploadInstallIdentityPath { get; private set; }

    public string? RunUploadClientStatePath { get; private set; }

    public string? RunUploadPrivateKeyPath { get; private set; }

    public void Initialize()
    {
        CardsJsonPath = CardJsonPathResolver.GetCardsJsonPath();
        RunLogDatabasePath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            RunLogSqliteSchema.DatabaseFileName
        );
        CombatReplayDirectoryPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            "CombatReplays"
        );
        RunUploadInstallIdentityPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            "install-id.txt"
        );
        RunUploadClientStatePath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            "run-upload-client.json"
        );
        RunUploadPrivateKeyPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            "run-upload-rsa.json"
        );
    }
}
