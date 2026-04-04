#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.PvpBattles.Persistence;

internal sealed class PvpBattleCatalog : IPvpBattleCatalog
{
    private readonly PvpBattleSqliteStore _store;

    public PvpBattleCatalog(string databasePath)
    {
        _store = new PvpBattleSqliteStore(databasePath);
    }

    public void Save(PvpBattleManifest manifest)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        _store.Save(manifest);
    }

    public void Delete(string battleId)
    {
        _store.Delete(battleId);
    }

    public PvpBattleManifest? TryLoad(string battleId)
    {
        return _store.TryLoad(battleId);
    }

    public IEnumerable<string> ListBattleIds()
    {
        return _store.ListBattleIds();
    }

    public IReadOnlyList<PvpBattleManifest> ListRecentBattles(int limit)
    {
        return _store.ListRecentBattles(limit);
    }
}
