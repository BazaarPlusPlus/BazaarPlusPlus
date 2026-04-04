#pragma warning disable CS0436
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json;

namespace BazaarPlusPlus;

internal sealed class MonsterDatabase : IMonsterCatalog
{
    private const string EmbeddedResourceName = "BazaarPlusPlus.Data.monsters_bazaardb.json";

    private readonly Dictionary<Guid, MonsterInfo> _db = new Dictionary<Guid, MonsterInfo>();
    private readonly Dictionary<string, MonsterInfo> _dbByShortEncounterId = new Dictionary<
        string,
        MonsterInfo
    >(StringComparer.OrdinalIgnoreCase);

    private sealed class MonsterRecordDto
    {
        [JsonProperty("encounter_id")]
        public string EncounterId { get; set; } = string.Empty;

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("base_tier")]
        public string BaseTier { get; set; } = string.Empty;

        [JsonProperty("rewards")]
        public MonsterRewardsDto? Rewards { get; set; }

        [JsonProperty("combatant")]
        public MonsterCombatantDto? Combatant { get; set; }

        [JsonProperty("monster_metadata")]
        public MonsterMetadataDto? MonsterMetadata { get; set; }
    }

    private sealed class MonsterRewardsDto
    {
        [JsonProperty("gold")]
        public int? Gold { get; set; }

        [JsonProperty("xp")]
        public int? Xp { get; set; }
    }

    private sealed class MonsterCombatantDto
    {
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("level")]
        public int? Level { get; set; }
    }

    private sealed class MonsterMetadataDto
    {
        [JsonProperty("available")]
        public string Available { get; set; } = string.Empty;

        [JsonProperty("day")]
        public int? Day { get; set; }

        [JsonProperty("health")]
        public int? Health { get; set; }

        [JsonProperty("board")]
        public List<MonsterBoardCardDto> Board { get; set; } = new List<MonsterBoardCardDto>();

        [JsonProperty("skills")]
        public List<MonsterSkillDto> Skills { get; set; } = new List<MonsterSkillDto>();
    }

    private sealed class MonsterBoardCardDto
    {
        [JsonProperty("cardid")]
        public string CardId { get; set; } = string.Empty;

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("tier")]
        public string Tier { get; set; } = string.Empty;

        [JsonProperty("size")]
        public string Size { get; set; } = string.Empty;

        [JsonProperty("enchant")]
        public string Enchant { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
    }

    private sealed class MonsterSkillDto
    {
        [JsonProperty("skill_id")]
        public string SkillId { get; set; } = string.Empty;

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("tier")]
        public string Tier { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
    }

    public void Initialize()
    {
        try
        {
            var json = ReadDatabaseJson(out var source);
            if (string.IsNullOrWhiteSpace(json))
            {
                BppLog.Warn("MonsterDatabase", "Monster database JSON was empty");
                _db.Clear();
                _dbByShortEncounterId.Clear();
                return;
            }

            var raw =
                JsonConvert.DeserializeObject<Dictionary<string, MonsterRecordDto>>(json)
                ?? new Dictionary<string, MonsterRecordDto>();

            _db.Clear();
            _dbByShortEncounterId.Clear();
            var invalidEncounterIds = 0;
            foreach (var pair in raw)
            {
                if (!Guid.TryParse(pair.Key, out var encounterId))
                {
                    invalidEncounterIds++;
                    continue;
                }

                var monster = MapMonster(encounterId, pair.Value);
                if (monster == null)
                    continue;

                _db[encounterId] = monster;
                _dbByShortEncounterId[GetShortEncounterId(pair.Key)] = monster;
            }

            BppLog.Info(
                "MonsterDatabase",
                $"Loaded {_db.Count} entries from {source} invalidEncounterIds={invalidEncounterIds} shortKeys={_dbByShortEncounterId.Count}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("MonsterDatabase", "Failed to load monster database", ex);
            _db.Clear();
            _dbByShortEncounterId.Clear();
        }
    }

    public bool TryGetByEncounterId(Guid encounterId, out MonsterInfo? monster)
    {
        var found = _db.TryGetValue(encounterId, out monster);
        BppLog.Debug("MonsterDatabase", $"Lookup encounterId={encounterId} found={found}");
        return found;
    }

    public bool TryGetByEncounterId(string encounterId, out MonsterInfo? monster)
    {
        monster = null;
        if (string.IsNullOrWhiteSpace(encounterId))
        {
            BppLog.Debug("MonsterDatabase", "Lookup encounterId=<empty> found=False");
            return false;
        }

        if (
            Guid.TryParse(encounterId, out var encounterGuid)
            && TryGetByEncounterId(encounterGuid, out monster)
        )
            return true;

        return TryGetByEncounterIdPrefix(encounterId, out monster);
    }

    public bool TryGetByEncounterIdPrefix(string encounterIdPrefix, out MonsterInfo? monster)
    {
        monster = null;
        var key = GetShortEncounterId(encounterIdPrefix);
        var found =
            !string.IsNullOrWhiteSpace(key) && _dbByShortEncounterId.TryGetValue(key, out monster);
        BppLog.Debug(
            "MonsterDatabase",
            $"Lookup encounterIdPrefix={encounterIdPrefix} normalized={key} found={found}"
        );
        return found;
    }

    public IReadOnlyCollection<MonsterInfo> GetAll()
    {
        return _db.Values.ToList();
    }

    private static string ReadDatabaseJson(out string source)
    {
        var assembly = typeof(MonsterDatabase).Assembly;
        using (var stream = assembly.GetManifestResourceStream(EmbeddedResourceName))
        {
            if (stream != null)
            {
                using (var reader = new StreamReader(stream))
                {
                    source = $"embedded:{EmbeddedResourceName}";
                    return reader.ReadToEnd();
                }
            }
        }

        var pluginPath = Path.Combine(Paths.PluginPath, "monsters_bazaardb.json");
        if (File.Exists(pluginPath))
        {
            source = pluginPath;
            return File.ReadAllText(pluginPath);
        }

        source = "none";
        return string.Empty;
    }

    private static MonsterInfo? MapMonster(Guid encounterId, MonsterRecordDto dto)
    {
        if (dto == null)
            return null;

        var boardCards =
            dto.MonsterMetadata?.Board?.Select(MapBoardCard)
                .Where(card => card != null)
                .Cast<MonsterBoardCardInfo>()
                .ToList()
            ?? new List<MonsterBoardCardInfo>();
        var skills =
            dto.MonsterMetadata?.Skills?.Select(MapSkill)
                .Where(skill => skill != null)
                .Cast<MonsterSkillInfo>()
                .ToList()
            ?? new List<MonsterSkillInfo>();

        return new MonsterInfo
        {
            EncounterId = encounterId,
            EncounterKey = dto.EncounterId ?? encounterId.ToString(),
            EncounterShortId = GetShortEncounterId(dto.EncounterId ?? encounterId.ToString()),
            Title = dto.Title ?? string.Empty,
            BaseTier = dto.BaseTier ?? string.Empty,
            CombatLevel = dto.Combatant?.Level,
            Health = dto.MonsterMetadata?.Health,
            RewardGold = dto.Rewards?.Gold,
            RewardXp = dto.Rewards?.Xp,
            BoardCards = boardCards,
            Skills = skills,
        };
    }

    private static MonsterBoardCardInfo? MapBoardCard(MonsterBoardCardDto dto)
    {
        if (dto == null || !Guid.TryParse(dto.CardId, out var cardId))
        {
            BppLog.Debug(
                "MonsterDatabase",
                $"Dropping invalid board card id={dto?.CardId ?? "null"}"
            );
            return null;
        }

        return new MonsterBoardCardInfo
        {
            CardId = cardId,
            Title = dto.Title ?? string.Empty,
            Tier = dto.Tier ?? string.Empty,
            Size = dto.Size ?? string.Empty,
            Enchant = dto.Enchant ?? string.Empty,
            Type = dto.Type ?? string.Empty,
        };
    }

    private static MonsterSkillInfo? MapSkill(MonsterSkillDto dto)
    {
        if (dto == null || !Guid.TryParse(dto.SkillId, out var skillId))
        {
            BppLog.Debug("MonsterDatabase", $"Dropping invalid skill id={dto?.SkillId ?? "null"}");
            return null;
        }

        return new MonsterSkillInfo
        {
            SkillId = skillId,
            Title = dto.Title ?? string.Empty,
            Tier = dto.Tier ?? string.Empty,
            Type = dto.Type ?? string.Empty,
        };
    }

    private static string GetShortEncounterId(string encounterId)
    {
        if (string.IsNullOrWhiteSpace(encounterId))
            return string.Empty;

        var dashIndex = encounterId.IndexOf('-');
        return dashIndex > 0 ? encounterId[..dashIndex] : encounterId;
    }
}
