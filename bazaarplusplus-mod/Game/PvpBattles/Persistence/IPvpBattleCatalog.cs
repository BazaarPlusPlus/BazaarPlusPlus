#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.PvpBattles.Persistence;

internal interface IPvpBattleCatalog
{
    void Save(PvpBattleManifest manifest);

    void Delete(string battleId);

    PvpBattleManifest? TryLoad(string battleId);

    IEnumerable<string> ListBattleIds();

    IReadOnlyList<PvpBattleManifest> ListRecentBattles(int limit);
}
