#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayPayloadStore
{
    private const string FileSuffix = ".payload.mpack.gz";
    private readonly string _rootPath;

    public CombatReplayPayloadStore(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Replay root path is required.", nameof(rootPath));

        _rootPath = rootPath;
        Directory.CreateDirectory(_rootPath);
    }

    public void Save(PvpReplayPayload payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (string.IsNullOrWhiteSpace(payload.BattleId))
            throw new ArgumentException("Battle id is required.", nameof(payload));

        Directory.CreateDirectory(_rootPath);
        var filePath = GetFilePath(payload.BattleId);
        WriteAllBytesAtomically(filePath, PvpReplayPayloadCodec.Serialize(payload));
    }

    public PvpReplayPayload? Load(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return null;

        var filePath = GetFilePath(battleId);
        if (!File.Exists(filePath))
            return null;

        try
        {
            var payloadBytes = File.ReadAllBytes(filePath);
            return PvpReplayPayloadCodec.Deserialize(payloadBytes);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayPayloadStore",
                $"Skipping unreadable replay payload '{filePath}': {ex.Message}"
            );
            return null;
        }
    }

    public bool Exists(string battleId)
    {
        return !string.IsNullOrWhiteSpace(battleId) && File.Exists(GetFilePath(battleId));
    }

    public void Delete(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return;

        var filePath = GetFilePath(battleId);
        if (!File.Exists(filePath))
            return;

        File.Delete(filePath);
    }

    public IEnumerable<string> ListBattleIds()
    {
        Directory.CreateDirectory(_rootPath);

        foreach (var filePath in Directory.EnumerateFiles(_rootPath, $"*{FileSuffix}"))
        {
            var fileName = Path.GetFileName(filePath);
            if (
                fileName.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase)
                && fileName.Length > FileSuffix.Length
            )
            {
                yield return fileName[..^FileSuffix.Length];
            }
        }
    }

    private string GetFilePath(string battleId)
    {
        return Path.Combine(_rootPath, $"{battleId}{FileSuffix}");
    }

    private static void WriteAllBytesAtomically(string filePath, byte[] bytes)
    {
        var directoryPath =
            Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("Payload path must have a parent directory.");
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
