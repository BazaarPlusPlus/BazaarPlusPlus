using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus;

public class RunInfo
{
    public String Hero;
    public List<SkillInfo> Skills;
    public List<SkillInfo> OppSkills;
    public List<CardInfo> Cards;
    public List<CardInfo> Stash;
    public List<CardInfo> OppCards;
    public List<CardInfo> OppStash;
    public int? OppHealth;
    public int? OppRegen;
    public int? OppGold;
    public int? OppIncome;
    public int? OppShield;
    public int? OppLevel;
    public string OppName;
    public uint Wins;
    public uint Losses;
    public int Day;
    public string Version = "0.0.1";
    public int? Health;
    public int? Shield;
    public int? Regen { get; set; }
    public int? Level { get; set; }
    public int? Gold { get; set; }
    public int? Income { get; set; }
    public string Name;
    public string OppHero;
    public int? Prestige;
    public int? OppPrestige;
    public bool PlayMode;
    public List<CardInfo> AvailableEncounters;
    public List<CardInfo> CurrentEncounterChoices;

    public class SkillInfo
    {
        public ETier Tier;
        public Guid TemplateId;
        public string Name;
        public Dictionary<ECardAttributeType, int> Attributes { get; set; } =
            new Dictionary<ECardAttributeType, int>();
    }

    public class MonsterPreview
    {
        public Guid EncounterTemplateId;
        public Guid EncounterId;
        public string EncounterShortId;
        public string EncounterName;
        public string Title;
        public string MonsterTemplateId;
        public int? CombatLevel;
        public int? Health;
        public int? RewardGold;
        public int? RewardXp;
        public bool? SandstormEnabled;
        public List<MonsterPreviewCard> BoardCards; // null = no local data
        public List<MonsterPreviewCard> Skills; // null = no local data
    }

    public class MonsterPreviewCard
    {
        public string TemplateId { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public int Tier { get; set; }
        public int Size { get; set; } = 1;
        public string Enchant { get; set; } = "None";
        public Dictionary<int, int> Attributes { get; set; } = new Dictionary<int, int>();
    }

    public class CardInfo
    {
        public ETier Tier;
        public string Name;
        public Guid TemplateId;
        public EContainerSocketId? Left;
        public InstanceId Instance;
        public HashSet<ECardTag> Tags;
        public Dictionary<ECardAttributeType, int> Attributes { get; set; } =
            new Dictionary<ECardAttributeType, int>();
        public string Enchant;
    }
}
