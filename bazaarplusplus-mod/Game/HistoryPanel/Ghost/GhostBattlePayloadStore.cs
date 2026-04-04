#nullable enable
using System;
using System.IO;
using BazaarPlusPlus.Game.ModApi;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal sealed class GhostBattlePayloadStore
{
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

        File.WriteAllText(
            GetFilePath(payload.BattleId),
            JsonConvert.SerializeObject(payload, ModApiSerialization.SerializerSettings)
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
            return JsonConvert.DeserializeObject<GhostBattlePayload>(
                File.ReadAllText(filePath),
                ModApiSerialization.SerializerSettings
            );
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
        return Path.Combine(_rootPath, $"{battleId}.ghost.json");
    }
}
