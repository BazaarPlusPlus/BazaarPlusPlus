#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus;

internal interface IMonsterCatalog
{
    bool TryGetByEncounterId(Guid encounterId, out MonsterInfo? monster);

    bool TryGetByEncounterId(string encounterId, out MonsterInfo? monster);

    bool TryGetByEncounterIdPrefix(string encounterIdPrefix, out MonsterInfo? monster);

    IReadOnlyCollection<MonsterInfo> GetAll();
}
