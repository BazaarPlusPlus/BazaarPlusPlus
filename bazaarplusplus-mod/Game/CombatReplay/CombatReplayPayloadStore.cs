#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayPayloadStore
{
    private readonly string _rootPath;

    public CombatReplayPayloadStore(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Replay root path is required.", nameof(rootPath));

        _rootPath = rootPath;
        Directory.CreateDirectory(_rootPath);
    }

    public void Save(BazaarPlusPlus.Game.PvpBattles.PvpReplayPayload payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (string.IsNullOrWhiteSpace(payload.BattleId))
            throw new ArgumentException("Battle id is required.", nameof(payload));

        Directory.CreateDirectory(_rootPath);
        var filePath = GetFilePath(payload.BattleId);
        var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
        File.WriteAllText(filePath, json);
    }

    public BazaarPlusPlus.Game.PvpBattles.PvpReplayPayload? Load(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return null;

        var filePath = GetFilePath(battleId);
        if (!File.Exists(filePath))
            return null;

        try
        {
            return JsonConvert.DeserializeObject<BazaarPlusPlus.Game.PvpBattles.PvpReplayPayload>(
                File.ReadAllText(filePath)
            );
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

        foreach (var filePath in Directory.EnumerateFiles(_rootPath, "*.payload.json"))
        {
            var fileName = Path.GetFileName(filePath);
            if (
                fileName.EndsWith(".payload.json", StringComparison.OrdinalIgnoreCase)
                && fileName.Length > ".payload.json".Length
            )
            {
                yield return fileName[..^".payload.json".Length];
            }
        }
    }

    private string GetFilePath(string battleId)
    {
        return Path.Combine(_rootPath, $"{battleId}.payload.json");
    }
}
