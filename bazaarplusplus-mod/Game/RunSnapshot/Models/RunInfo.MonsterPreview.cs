using System;
using System.Collections.Generic;

namespace BazaarPlusPlus;

public partial class RunInfo
{
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
        public List<MonsterPreviewCard> BoardCards;
        public List<MonsterPreviewCard> Skills;
    }
}
