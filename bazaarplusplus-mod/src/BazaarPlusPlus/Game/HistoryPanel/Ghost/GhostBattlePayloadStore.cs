#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal sealed class GhostBattlePayloadStore
{
    private const string FileSuffix = ".ghost.mpack.gz";
    private const string DirectoryName = "GhostBattlePayloads";
    private readonly FileBackedPayloadStore<GhostBattlePayload> _store;

    // Resolves the ghost-payload directory as a sibling of the combat replay directory, falling
    // back to a child directory when the replay path has no parent. Single source of truth for
    // every call site that needs to construct a GhostBattlePayloadStore from the replay root.
    public static string ResolveDirectory(string replayDirectoryPath)
    {
        var parentDirectory = Path.GetDirectoryName(replayDirectoryPath);
        return string.IsNullOrWhiteSpace(parentDirectory)
            ? Path.Combine(replayDirectoryPath, DirectoryName)
            : Path.Combine(parentDirectory, DirectoryName);
    }

    public GhostBattlePayloadStore(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        _store = new FileBackedPayloadStore<GhostBattlePayload>(
            rootPath,
            FileSuffix,
            GhostBattlePayloadCodec.Serialize,
            GhostBattlePayloadCodec.TryDeserialize
        );
    }

    public void Save(GhostBattlePayload payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (string.IsNullOrWhiteSpace(payload.BattleId))
            throw new ArgumentException("Battle id is required.", nameof(payload));

        _store.Save(payload.BattleId, payload);
    }

    public GhostBattlePayload? Load(string battleId)
    {
        return _store.Load(battleId);
    }

    internal FileBackedPayloadLoadResult<GhostBattlePayload> LoadDetailed(string battleId)
    {
        return _store.LoadDetailed(battleId);
    }

    public void Delete(string battleId)
    {
        _store.Delete(battleId);
    }
}
