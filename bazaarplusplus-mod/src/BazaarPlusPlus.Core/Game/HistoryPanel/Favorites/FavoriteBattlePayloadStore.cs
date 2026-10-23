#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Game.HistoryPanel.Favorites;

public sealed class FavoriteBattlePayloadStore
{
    private const string FileSuffix = ".favorite.mpack.gz";
    private const int MaximumFileBytes = 16 * 1024 * 1024;
    private readonly string _rootPath;
    private readonly FileBackedPayloadStore<FavoriteBattlePayload> _store;

    // The combat replay directory is PathConstants.CombatReplays(dataRoot), so its parent is the
    // Data Root that holds FavoriteBattles/ too.
    public static string ResolveDirectory(string replayDirectoryPath)
    {
        var dataRoot = Path.GetDirectoryName(replayDirectoryPath);
        return PathConstants.FavoriteBattles(
            string.IsNullOrWhiteSpace(dataRoot) ? replayDirectoryPath : dataRoot
        );
    }

    public FavoriteBattlePayloadStore(string rootPath)
    {
        _rootPath = rootPath;
        _store = new FileBackedPayloadStore<FavoriteBattlePayload>(
            rootPath,
            FileSuffix,
            Serialize,
            TryDeserialize,
            MaximumFileBytes
        );
    }

    public void Save(FavoriteBattlePayload payload) => _store.Save(payload.BattleId, payload);

    public FileBackedPayloadLoadResult<FavoriteBattlePayload> LoadDetailed(string battleId) =>
        _store.LoadDetailed(battleId);

    public void Delete(string battleId) => _store.Delete(battleId);

    public IEnumerable<string> ListIds() => _store.ListIds();

    public DateTime LastWriteTimeUtc(string battleId) =>
        File.GetLastWriteTimeUtc(Path.Combine(_rootPath, battleId + FileSuffix));

    // A favorite's card facets are stored by member name (ADR-0015 §4).
    private static byte[] Serialize(FavoriteBattlePayload payload) =>
        MessagePackGzipCodec.Serialize(payload, enumsAsNames: true);

    private static bool TryDeserialize(
        byte[]? payloadBytes,
        out FavoriteBattlePayload? payload,
        out string? error,
        out Exception? exception
    ) =>
        MessagePackGzipCodec.TryDeserialize(
            payloadBytes,
            out payload,
            out error,
            out exception,
            64 * 1024 * 1024,
            enumsAsNames: true
        );
}
