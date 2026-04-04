#pragma warning disable CS0436
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus;

internal sealed class MonsterInfo
{
    public Guid EncounterId { get; set; }

    public string EncounterKey { get; set; } = string.Empty;

    public string EncounterShortId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string BaseTier { get; set; } = string.Empty;

    public int? CombatLevel { get; set; }

    public int? Health { get; set; }

    public int? RewardGold { get; set; }

    public int? RewardXp { get; set; }

    public IReadOnlyList<MonsterBoardCardInfo> BoardCards { get; set; } =
        Array.Empty<MonsterBoardCardInfo>();

    public IReadOnlyList<MonsterSkillInfo> Skills { get; set; } = Array.Empty<MonsterSkillInfo>();
}

internal sealed class MonsterBoardCardInfo
{
    public Guid CardId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Tier { get; set; } = string.Empty;

    public string Size { get; set; } = string.Empty;

    public string Enchant { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;
}

internal sealed class MonsterSkillInfo
{
    public Guid SkillId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Tier { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;
}
