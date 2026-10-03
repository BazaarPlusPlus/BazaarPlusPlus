#nullable enable
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayPayloadStore
{
    private const string FileSuffix = ".payload.mpack.gz";
    private readonly string _rootPath;
    private readonly FileBackedPayloadStore<PvpReplayPayload> _store;

    public CombatReplayPayloadStore(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Replay root path is required.", nameof(rootPath));

        _rootPath = rootPath;
        _store = new FileBackedPayloadStore<PvpReplayPayload>(
            rootPath,
            FileSuffix,
            PvpReplayPayloadCodec.Serialize,
            PvpReplayPayloadCodec.TryDeserialize
        );
    }

    public void Save(PvpReplayPayload payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (string.IsNullOrWhiteSpace(payload.BattleId))
            throw new ArgumentException("Battle id is required.", nameof(payload));

        Directory.CreateDirectory(_rootPath);
        _store.Save(payload.BattleId, payload);
    }

    public PvpReplayPayload? Load(string battleId)
    {
        return _store.Load(battleId);
    }

    internal FileBackedPayloadLoadResult<PvpReplayPayload> LoadDetailed(string battleId)
    {
        return _store.LoadDetailed(battleId);
    }

    public bool Exists(string battleId)
    {
        return _store.Exists(battleId);
    }

    public void Delete(string battleId)
    {
        _store.Delete(battleId);
    }

    public IEnumerable<string> ListBattleIds()
    {
        return _store.ListIds();
    }
}
