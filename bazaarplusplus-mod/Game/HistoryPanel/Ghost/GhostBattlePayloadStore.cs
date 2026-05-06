#nullable enable
using System;
using System.IO;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal sealed class GhostBattlePayloadStore
{
    private const string FileSuffix = ".ghost.mpack.gz";
    private readonly string _rootPath;

    public GhostBattlePayloadStore(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        _rootPath = rootPath;
        Directory.CreateDirectory(_rootPath);
    }

    public void Save(GhostBattlePayload payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (string.IsNullOrWhiteSpace(payload.BattleId))
            throw new ArgumentException("Battle id is required.", nameof(payload));

        WriteAllBytesAtomically(
            GetFilePath(payload.BattleId),
            GhostBattlePayloadCodec.Serialize(payload)
        );
    }

    public GhostBattlePayload? Load(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return null;

        var filePath = GetFilePath(battleId);
        if (!File.Exists(filePath))
            return null;

        try
        {
            var payloadBytes = File.ReadAllBytes(filePath);
            return GhostBattlePayloadCodec.Deserialize(payloadBytes);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "GhostBattlePayloadStore",
                $"Skipping unreadable ghost payload '{filePath}': {ex.Message}"
            );
            return null;
        }
    }

    public void Delete(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return;

        var filePath = GetFilePath(battleId);
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private string GetFilePath(string battleId)
    {
        return Path.Combine(_rootPath, $"{battleId}{FileSuffix}");
    }

    private static void WriteAllBytesAtomically(string filePath, byte[] bytes)
    {
        var directoryPath =
            Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException(
                "Ghost payload path must have a parent directory."
            );
        var tempPath = Path.Combine(
            directoryPath,
            $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp"
        );
        File.WriteAllBytes(tempPath, bytes);
        try
        {
            if (File.Exists(filePath))
                File.Replace(tempPath, filePath, null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, filePath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
