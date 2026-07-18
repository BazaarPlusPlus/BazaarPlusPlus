#nullable enable
using System.Globalization;
using System.Text;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.EventPreview;

internal sealed class EncounterPreviewCacheStore
{
    public const int SchemaVersion = 3;
    internal const int MaxCacheFileBytes = 32 * 1024 * 1024;

    private static readonly UTF8Encoding Utf8NoBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Include,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
        MaxDepth = 128,
    };

    private readonly object _syncRoot = new();

    public EncounterPreviewCacheStore(string cachePath)
    {
        CachePath = !string.IsNullOrWhiteSpace(cachePath)
            ? cachePath
            : throw new ArgumentException("Cache path is required.", nameof(cachePath));
    }

    public string CachePath { get; }

    public void Save(EncounterPreviewCacheIdentity identity, EncounterPreviewSnapshot snapshot)
    {
        if (identity == null)
            throw new ArgumentNullException(nameof(identity));
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        var document = ToWire(identity, snapshot);
        var json = JsonConvert.SerializeObject(document, SerializerSettings);
        var bytes = Utf8NoBom.GetBytes(json);
        if (bytes.Length > MaxCacheFileBytes)
        {
            throw new InvalidOperationException(
                $"Encounter-preview cache is too large to persist ({bytes.Length} bytes)."
            );
        }

        lock (_syncRoot)
        {
            CleanupOrphanedTempFilesCore();
            AtomicFileWriter.Write(CachePath, bytes);
        }
    }

    public bool TryLoad(
        EncounterPreviewCacheIdentity identity,
        out EncounterPreviewSnapshot? snapshot,
        out string missReason
    )
    {
        if (identity == null)
            throw new ArgumentNullException(nameof(identity));

        snapshot = null;
        missReason = string.Empty;

        lock (_syncRoot)
        {
            CleanupOrphanedTempFilesCore();
            if (!File.Exists(CachePath))
            {
                missReason = "not-found";
                return false;
            }

            try
            {
                if (new FileInfo(CachePath).Length > MaxCacheFileBytes)
                {
                    missReason = "file-too-large";
                    return false;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                missReason = "read-failed";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(CachePath, Utf8NoBom);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                missReason = "read-failed";
                return false;
            }

            CacheDocumentWire? document;
            try
            {
                document = JsonConvert.DeserializeObject<CacheDocumentWire>(
                    json,
                    SerializerSettings
                );
            }
            catch (Exception ex)
                when (ex is JsonException
                    || ex is ArgumentException
                    || ex is FormatException
                    || ex is OverflowException
                )
            {
                missReason = "invalid-json";
                return false;
            }

            if (document == null)
            {
                missReason = "invalid-json";
                return false;
            }
            if (document.SchemaVersion != SchemaVersion)
            {
                missReason = "schema-mismatch";
                return false;
            }
            if (document.Identity == null)
            {
                missReason = "invalid-json";
                return false;
            }

            EncounterPreviewCacheIdentity storedIdentity;
            try
            {
                storedIdentity = FromWire(document.Identity);
            }
            catch (Exception ex) when (IsInvalidPayloadException(ex))
            {
                missReason = "invalid-json";
                return false;
            }

            if (!identity.Equals(storedIdentity))
            {
                missReason = "identity-mismatch";
                return false;
            }

            try
            {
                snapshot = FromWire(document);
                return true;
            }
            catch (Exception ex) when (IsInvalidPayloadException(ex))
            {
                snapshot = null;
                missReason = "invalid-json";
                return false;
            }
        }
    }

    public void CleanupOrphanedTempFiles()
    {
        lock (_syncRoot)
            CleanupOrphanedTempFilesCore();
    }

    private void CleanupOrphanedTempFilesCore()
    {
        var directory = Path.GetDirectoryName(CachePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        var pattern = Path.GetFileName(CachePath) + ".*.tmp";
        try
        {
            foreach (var tempPath in Directory.EnumerateFiles(directory, pattern))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Best effort: a stale temp file must never make a valid snapshot unreadable.
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Best effort for the same reason as the individual delete above.
        }
    }

    private static CacheDocumentWire ToWire(
        EncounterPreviewCacheIdentity identity,
        EncounterPreviewSnapshot snapshot
    )
    {
        return new CacheDocumentWire
        {
            SchemaVersion = SchemaVersion,
            Identity = ToWire(identity),
            Events = snapshot
                .Events.OrderBy(eventPlan => eventPlan.TemplateId)
                .Select(ToWire)
                .ToList(),
            LevelUps = snapshot.LevelUps.OrderBy(plan => plan.Level).Select(ToWire).ToList(),
            Templates = snapshot
                .Templates.OrderBy(templatePlan => templatePlan.TemplateId)
                .Select(ToWire)
                .ToList(),
            Coverage = ToWire(snapshot.Coverage),
        };
    }

    private static EncounterPreviewSnapshot FromWire(CacheDocumentWire document)
    {
        if (
            document.Events == null
            || document.LevelUps == null
            || document.Templates == null
            || document.Coverage == null
        )
            throw new FormatException("Encounter-preview cache payload arrays are missing.");

        var events = new EncounterPreviewEventPlan[document.Events.Count];
        for (var i = 0; i < events.Length; i++)
            events[i] = FromWire(Required(document.Events[i], "event plan"));

        var templates = new EncounterPreviewTemplatePlan[document.Templates.Count];
        for (var i = 0; i < templates.Length; i++)
            templates[i] = FromWire(Required(document.Templates[i], "template plan"));

        var levelUps = new LevelUpPreviewPlan[document.LevelUps.Count];
        for (var i = 0; i < levelUps.Length; i++)
            levelUps[i] = FromWire(Required(document.LevelUps[i], "level-up plan"));

        return new EncounterPreviewSnapshot(
            events,
            templates,
            levelUps,
            FromWire(document.Coverage)
        );
    }

    private static CacheIdentityWire ToWire(EncounterPreviewCacheIdentity identity) =>
        new()
        {
            Kind = identity.Kind,
            Resource = identity.Resource,
            Value = identity.Value,
            GameBuild = identity.GameBuild,
            BuildChannel = identity.BuildChannel,
        };

    private static EncounterPreviewCacheIdentity FromWire(CacheIdentityWire identity) =>
        new(
            Required(identity.Kind, "identity kind"),
            Required(identity.Resource, "identity resource"),
            Required(identity.Value, "identity value"),
            Required(identity.GameBuild, "identity game build"),
            Required(identity.BuildChannel, "identity build channel")
        );

    private static EventPlanWire ToWire(EncounterPreviewEventPlan plan) =>
        new()
        {
            TemplateId = FormatGuid(plan.TemplateId),
            IsRandomSelectionEvent = plan.IsRandomSelectionEvent,
            SuppressRandomOutcome = plan.SuppressRandomOutcome,
            ChoiceLimit = plan.ChoiceLimit,
            OutcomeGroups = plan.OutcomeGroups.Select(ToWire).ToList(),
            ChoiceGroups = plan.ChoiceGroups.Select(ToWire).ToList(),
        };

    private static EncounterPreviewEventPlan FromWire(EventPlanWire plan)
    {
        if (plan.OutcomeGroups == null || plan.ChoiceGroups == null)
            throw new FormatException("Event-plan groups are missing.");

        return new EncounterPreviewEventPlan(
            ParseGuid(plan.TemplateId, "event template id"),
            plan.IsRandomSelectionEvent,
            plan.SuppressRandomOutcome,
            plan.ChoiceLimit,
            plan.OutcomeGroups.Select(group => FromWire(Required(group, "outcome group")))
                .ToArray(),
            plan.ChoiceGroups.Select(group => FromWire(Required(group, "choice group"))).ToArray()
        );
    }

    private static LevelUpPlanWire ToWire(LevelUpPreviewPlan plan) =>
        new()
        {
            Level = plan.Level,
            HealthIncrease = plan.HealthIncrease,
            IsRandomSelection = plan.IsRandomSelection,
            Groups = plan.Groups.Select(ToWire).ToList(),
        };

    private static LevelUpPreviewPlan FromWire(LevelUpPlanWire plan)
    {
        if (plan.Groups == null)
            throw new FormatException("Level-up groups are missing.");
        return new LevelUpPreviewPlan(
            plan.Level,
            plan.HealthIncrease,
            plan.IsRandomSelection,
            plan.Groups.Select(group => FromWire(Required(group, "level-up group"))).ToArray()
        );
    }

    private static LevelUpGroupWire ToWire(LevelUpPreviewGroup group) =>
        new()
        {
            RandomWeight = group.RandomWeight,
            Limit = group.Limit,
            TemplateIds = group.TemplateIds.Select(FormatGuid).ToList(),
            HeroConditions = group.HeroConditions.Select(ToWire).ToList(),
        };

    private static LevelUpPreviewGroup FromWire(LevelUpGroupWire group)
    {
        if (group.TemplateIds == null || group.HeroConditions == null)
            throw new FormatException("Level-up group collections are missing.");
        return new LevelUpPreviewGroup(
            group.RandomWeight,
            group.Limit,
            group.TemplateIds.Select(value => ParseGuid(value, "level-up template id")).ToArray(),
            group
                .HeroConditions.Select(condition => FromWire(Required(condition, "hero condition")))
                .ToArray()
        );
    }

    private static LevelUpHeroConditionWire ToWire(LevelUpPreviewHeroCondition condition) =>
        new()
        {
            Heroes = condition.Heroes.Select(hero => (int)hero).ToList(),
            ComparisonOperator = condition.ComparisonOperator,
        };

    private static LevelUpPreviewHeroCondition FromWire(LevelUpHeroConditionWire condition)
    {
        if (condition.Heroes == null)
            throw new FormatException("Level-up hero condition heroes are missing.");
        return new LevelUpPreviewHeroCondition(
            condition.Heroes.Select(value => ReadEnum<EHero>(value, "hero")).ToArray(),
            Required(condition.ComparisonOperator, "hero comparison operator")
        );
    }

    private static CoverageWire ToWire(EventPreviewCoverage coverage) =>
        new()
        {
            EventFailureCount = coverage.EventFailureCount,
            LevelUpFailureCount = coverage.LevelUpFailureCount,
            UnsupportedLevelUpPartCount = coverage.UnsupportedLevelUpPartCount,
            MissingReferencedTemplateCount = coverage.MissingReferencedTemplateCount,
        };

    private static EventPreviewCoverage FromWire(CoverageWire coverage) =>
        new(
            coverage.EventFailureCount,
            coverage.LevelUpFailureCount,
            coverage.UnsupportedLevelUpPartCount,
            coverage.MissingReferencedTemplateCount
        );

    private static TemplatePlanWire ToWire(EncounterPreviewTemplatePlan plan) =>
        new()
        {
            TemplateId = FormatGuid(plan.TemplateId),
            Kind = (int)plan.Kind,
            Heroes = plan.Heroes.Select(hero => (int)hero).ToList(),
            InternalName = plan.InternalName,
            Title = ToWire(plan.Title),
            Description = ToWire(plan.Description),
            AbilityValues = plan
                .AbilityValues.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new AbilityValueWire
                {
                    Key = pair.Key,
                    ValueText = pair.Value.ValueText,
                    Unit = pair.Value.Unit,
                })
                .ToList(),
            RewardFilter = ToWire(plan.RewardFilter),
        };

    private static EncounterPreviewTemplatePlan FromWire(TemplatePlanWire plan)
    {
        if (plan.Heroes == null || plan.AbilityValues == null)
            throw new FormatException("Template-plan collections are missing.");
        if (!Enum.IsDefined(typeof(EncounterPreviewTemplateKind), plan.Kind))
            throw new FormatException($"Unknown template kind '{plan.Kind}'.");

        var abilityValues = new Dictionary<string, EncounterPreviewAbilityValue>(
            plan.AbilityValues.Count,
            StringComparer.Ordinal
        );
        foreach (var value in plan.AbilityValues)
        {
            var required = Required(value, "ability value");
            var key = Required(required.Key, "ability key");
            if (
                !abilityValues.TryAdd(
                    key,
                    new EncounterPreviewAbilityValue(
                        Required(required.ValueText, "ability value text"),
                        required.Unit
                    )
                )
            )
                throw new FormatException($"Duplicate ability key '{key}'.");
        }

        return new EncounterPreviewTemplatePlan(
            ParseGuid(plan.TemplateId, "template id"),
            (EncounterPreviewTemplateKind)plan.Kind,
            plan.Heroes.Select(value => ReadEnum<EHero>(value, "hero")).ToArray(),
            plan.InternalName,
            FromWire(plan.Title),
            FromWire(plan.Description),
            abilityValues,
            FromWire(plan.RewardFilter)
        );
    }

    private static LocalizedTextWire ToWire(EncounterPreviewLocalizedText text) =>
        new() { Key = text.Key, FallbackText = text.FallbackText };

    private static EncounterPreviewLocalizedText FromWire(LocalizedTextWire? text) =>
        text == null
            ? new EncounterPreviewLocalizedText(null, null)
            : new EncounterPreviewLocalizedText(text.Key, text.FallbackText);

    private static OutcomeGroupWire ToWire(EncounterOutcomeGroupData group) =>
        new()
        {
            Weight = group.Weight,
            Ids = group.Ids.Select(FormatGuid).ToList(),
            QueryPools = group.QueryPools.Select(ToWire).ToList(),
            Requirements = group.Requirements.Select(ToWire).ToList(),
            DayCondition = ToWire(group.DayCondition),
        };

    private static EncounterOutcomeGroupData FromWire(OutcomeGroupWire group)
    {
        if (group.Ids == null || group.QueryPools == null || group.Requirements == null)
            throw new FormatException("Outcome-group collections are missing.");

        return new EncounterOutcomeGroupData(
            group.Weight,
            group.Ids.Select(value => ParseGuid(value, "outcome template id")).ToArray(),
            group.QueryPools.Select(pool => FromWire(Required(pool, "query pool"))).ToArray(),
            group.Requirements.Select(value => FromWire(Required(value, "requirement"))).ToArray(),
            FromWire(group.DayCondition)
        );
    }

    private static QueryPoolWire ToWire(EncounterOutcomeQueryPool pool) =>
        new() { Filter = ToWire(pool.Filter), Quantity = pool.Quantity };

    private static EncounterOutcomeQueryPool FromWire(QueryPoolWire pool) =>
        new(FromWire(pool.Filter), pool.Quantity);

    private static ChoiceGroupWire ToWire(EncounterChoiceGroupData group) =>
        new()
        {
            IsRandomPool = group.IsRandomPool,
            Members = group.Members.Select(ToWire).ToList(),
            DayCondition = ToWire(group.DayCondition),
        };

    private static EncounterChoiceGroupData FromWire(ChoiceGroupWire group)
    {
        if (group.Members == null)
            throw new FormatException("Choice-group members are missing.");

        return new EncounterChoiceGroupData(
            group.IsRandomPool,
            group.Members.Select(member => FromWire(Required(member, "step reference"))).ToArray(),
            FromWire(group.DayCondition)
        );
    }

    private static StepReferenceWire ToWire(EncounterStepReference reference) =>
        new()
        {
            TemplateId = FormatGuid(reference.TemplateId),
            Requirements = reference.Requirements.Select(ToWire).ToList(),
        };

    private static EncounterStepReference FromWire(StepReferenceWire reference)
    {
        if (reference.Requirements == null)
            throw new FormatException("Step-reference requirements are missing.");

        return new EncounterStepReference(
            ParseGuid(reference.TemplateId, "step template id"),
            reference
                .Requirements.Select(value => FromWire(Required(value, "requirement")))
                .ToArray()
        );
    }

    private static RequirementWire ToWire(EncounterCardRequirement requirement) =>
        new()
        {
            Ids = requirement.Ids.Select(FormatGuid).ToList(),
            TagCandidateGroups = requirement
                .TagCandidateGroups.Select(group => group.ToList())
                .ToList(),
            TagOperator = requirement.TagOperator,
            Comparison = requirement.Comparison,
            Amount = requirement.Amount,
        };

    private static EncounterCardRequirement FromWire(RequirementWire requirement)
    {
        if (requirement.Ids == null || requirement.TagCandidateGroups == null)
            throw new FormatException("Requirement collections are missing.");

        return new EncounterCardRequirement(
            requirement.Ids.Select(value => ParseGuid(value, "requirement template id")).ToArray(),
            requirement
                .TagCandidateGroups.Select(group =>
                    Required(group, "tag candidate group")
                        .Select(value => Required(value, "tag candidate"))
                        .ToArray()
                )
                .ToArray(),
            Required(requirement.TagOperator, "tag operator"),
            Required(requirement.Comparison, "comparison"),
            requirement.Amount
        );
    }

    private static DayConditionWire? ToWire(EncounterDayCondition? condition) =>
        condition is { } value
            ? new DayConditionWire { Day = value.Day, Comparison = value.Comparison }
            : null;

    private static EncounterDayCondition? FromWire(DayConditionWire? condition) =>
        condition == null
            ? null
            : new EncounterDayCondition(
                condition.Day,
                Required(condition.Comparison, "day comparison")
            );

    private static RewardFilterWire? ToWire(EncounterRewardFilter? filter) =>
        filter == null
            ? null
            : new RewardFilterWire
            {
                CardType = (int)filter.CardType,
                Quantity = filter.Quantity,
                FromAnyHero = filter.FromAnyHero,
                Sizes = filter.Sizes.Select(value => (int)value).ToList(),
                Tiers = filter.Tiers.Select(value => (int)value).ToList(),
                Tags = filter.Tags.Select(value => (int)value).ToList(),
                Keywords = filter.Keywords.Select(value => (int)value).ToList(),
                FilterSummary = filter.FilterSummary,
                ExcludedTags = filter.ExcludedTags.Select(value => (int)value).ToList(),
                ExcludedKeywords = filter.ExcludedKeywords.Select(value => (int)value).ToList(),
                UsesDayTierTable = filter.UsesDayTierTable,
                UsesDayTierDistribution = filter.UsesDayTierDistribution,
            };

    private static EncounterRewardFilter? FromWire(RewardFilterWire? filter)
    {
        if (filter == null)
            return null;
        if (
            filter.Sizes == null
            || filter.Tiers == null
            || filter.Tags == null
            || filter.Keywords == null
            || filter.ExcludedTags == null
            || filter.ExcludedKeywords == null
        )
            throw new FormatException("Reward-filter collections are missing.");

        return new EncounterRewardFilter(
            ReadEnum<ECardType>(filter.CardType, "card type"),
            filter.Quantity,
            filter.FromAnyHero,
            filter.Sizes.Select(value => ReadEnum<ECardSize>(value, "card size")).ToArray(),
            filter.Tiers.Select(value => ReadEnum<ETier>(value, "tier")).ToArray(),
            filter.Tags.Select(value => ReadEnum<ECardTag>(value, "card tag")).ToArray(),
            filter.Keywords.Select(value => ReadEnum<EHiddenTag>(value, "hidden tag")).ToArray(),
            filter.FilterSummary ?? string.Empty,
            filter
                .ExcludedTags.Select(value => ReadEnum<ECardTag>(value, "excluded card tag"))
                .ToArray(),
            filter
                .ExcludedKeywords.Select(value =>
                    ReadEnum<EHiddenTag>(value, "excluded hidden tag")
                )
                .ToArray(),
            filter.UsesDayTierTable,
            filter.UsesDayTierDistribution
        );
    }

    private static TEnum ReadEnum<TEnum>(int value, string label)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(typeof(TEnum), value))
            throw new FormatException($"Unknown {label} value '{value}'.");
        return (TEnum)Enum.ToObject(typeof(TEnum), value);
    }

    private static string FormatGuid(Guid value) => value.ToString("D");

    private static Guid ParseGuid(string? value, string label) =>
        Guid.TryParseExact(value, "D", out var parsed)
            ? parsed
            : throw new FormatException($"Invalid {label}.");

    private static T Required<T>(T? value, string label)
        where T : class => value ?? throw new FormatException($"Missing {label}.");

    private static bool IsInvalidPayloadException(Exception ex) =>
        ex is JsonException
        || ex is ArgumentException
        || ex is FormatException
        || ex is OverflowException
        || ex is InvalidOperationException;

    private sealed class CacheDocumentWire
    {
        [JsonProperty("schemaVersion", Order = 0)]
        public int SchemaVersion { get; set; }

        [JsonProperty("identity", Order = 1)]
        public CacheIdentityWire? Identity { get; set; }

        [JsonProperty("events", Order = 2)]
        public List<EventPlanWire>? Events { get; set; }

        [JsonProperty("levelUps", Order = 3)]
        public List<LevelUpPlanWire>? LevelUps { get; set; }

        [JsonProperty("templates", Order = 4)]
        public List<TemplatePlanWire>? Templates { get; set; }

        [JsonProperty("coverage", Order = 5)]
        public CoverageWire? Coverage { get; set; }
    }

    private sealed class CacheIdentityWire
    {
        [JsonProperty("kind", Order = 0)]
        public string? Kind { get; set; }

        [JsonProperty("resource", Order = 1)]
        public string? Resource { get; set; }

        [JsonProperty("value", Order = 2)]
        public string? Value { get; set; }

        [JsonProperty("gameBuild", Order = 3)]
        public string? GameBuild { get; set; }

        [JsonProperty("buildChannel", Order = 4)]
        public string? BuildChannel { get; set; }
    }

    private sealed class EventPlanWire
    {
        [JsonProperty("templateId", Order = 0)]
        public string? TemplateId { get; set; }

        [JsonProperty("isRandomSelectionEvent", Order = 1)]
        public bool IsRandomSelectionEvent { get; set; }

        [JsonProperty("suppressRandomOutcome", Order = 2)]
        public bool SuppressRandomOutcome { get; set; }

        [JsonProperty("choiceLimit", Order = 3)]
        public int? ChoiceLimit { get; set; }

        [JsonProperty("outcomeGroups", Order = 4)]
        public List<OutcomeGroupWire>? OutcomeGroups { get; set; }

        [JsonProperty("choiceGroups", Order = 5)]
        public List<ChoiceGroupWire>? ChoiceGroups { get; set; }
    }

    private sealed class LevelUpPlanWire
    {
        [JsonProperty("level", Order = 0)]
        public int Level { get; set; }

        [JsonProperty("healthIncrease", Order = 1)]
        public int HealthIncrease { get; set; }

        [JsonProperty("isRandomSelection", Order = 2)]
        public bool IsRandomSelection { get; set; }

        [JsonProperty("groups", Order = 3)]
        public List<LevelUpGroupWire>? Groups { get; set; }
    }

    private sealed class LevelUpGroupWire
    {
        [JsonProperty("randomWeight", Order = 0)]
        public uint RandomWeight { get; set; }

        [JsonProperty("limit", Order = 1)]
        public int Limit { get; set; }

        [JsonProperty("templateIds", Order = 2)]
        public List<string>? TemplateIds { get; set; }

        [JsonProperty("heroConditions", Order = 3)]
        public List<LevelUpHeroConditionWire>? HeroConditions { get; set; }
    }

    private sealed class LevelUpHeroConditionWire
    {
        [JsonProperty("heroes", Order = 0)]
        public List<int>? Heroes { get; set; }

        [JsonProperty("comparisonOperator", Order = 1)]
        public string? ComparisonOperator { get; set; }
    }

    private sealed class CoverageWire
    {
        [JsonProperty("eventFailureCount", Order = 0)]
        public int EventFailureCount { get; set; }

        [JsonProperty("levelUpFailureCount", Order = 1)]
        public int LevelUpFailureCount { get; set; }

        [JsonProperty("unsupportedLevelUpPartCount", Order = 2)]
        public int UnsupportedLevelUpPartCount { get; set; }

        [JsonProperty("missingReferencedTemplateCount", Order = 3)]
        public int MissingReferencedTemplateCount { get; set; }
    }

    private sealed class TemplatePlanWire
    {
        [JsonProperty("templateId", Order = 0)]
        public string? TemplateId { get; set; }

        [JsonProperty("kind", Order = 1)]
        public int Kind { get; set; }

        [JsonProperty("heroes", Order = 2)]
        public List<int>? Heroes { get; set; }

        [JsonProperty("internalName", Order = 3)]
        public string? InternalName { get; set; }

        [JsonProperty("title", Order = 4)]
        public LocalizedTextWire? Title { get; set; }

        [JsonProperty("description", Order = 5)]
        public LocalizedTextWire? Description { get; set; }

        [JsonProperty("abilityValues", Order = 6)]
        public List<AbilityValueWire>? AbilityValues { get; set; }

        [JsonProperty("rewardFilter", Order = 7)]
        public RewardFilterWire? RewardFilter { get; set; }
    }

    private sealed class LocalizedTextWire
    {
        [JsonProperty("key", Order = 0)]
        public string? Key { get; set; }

        [JsonProperty("fallbackText", Order = 1)]
        public string? FallbackText { get; set; }
    }

    private sealed class AbilityValueWire
    {
        [JsonProperty("key", Order = 0)]
        public string? Key { get; set; }

        [JsonProperty("valueText", Order = 1)]
        public string? ValueText { get; set; }

        [JsonProperty("unit", Order = 2)]
        public string? Unit { get; set; }
    }

    private sealed class OutcomeGroupWire
    {
        [JsonProperty("weight", Order = 0)]
        public uint Weight { get; set; }

        [JsonProperty("ids", Order = 1)]
        public List<string>? Ids { get; set; }

        [JsonProperty("queryPools", Order = 2)]
        public List<QueryPoolWire>? QueryPools { get; set; }

        [JsonProperty("requirements", Order = 3)]
        public List<RequirementWire>? Requirements { get; set; }

        [JsonProperty("dayCondition", Order = 4)]
        public DayConditionWire? DayCondition { get; set; }
    }

    private sealed class QueryPoolWire
    {
        [JsonProperty("filter", Order = 0)]
        public RewardFilterWire? Filter { get; set; }

        [JsonProperty("quantity", Order = 1)]
        public int? Quantity { get; set; }
    }

    private sealed class ChoiceGroupWire
    {
        [JsonProperty("isRandomPool", Order = 0)]
        public bool IsRandomPool { get; set; }

        [JsonProperty("members", Order = 1)]
        public List<StepReferenceWire>? Members { get; set; }

        [JsonProperty("dayCondition", Order = 2)]
        public DayConditionWire? DayCondition { get; set; }
    }

    private sealed class StepReferenceWire
    {
        [JsonProperty("templateId", Order = 0)]
        public string? TemplateId { get; set; }

        [JsonProperty("requirements", Order = 1)]
        public List<RequirementWire>? Requirements { get; set; }
    }

    private sealed class RequirementWire
    {
        [JsonProperty("ids", Order = 0)]
        public List<string>? Ids { get; set; }

        [JsonProperty("tagCandidateGroups", Order = 1)]
        public List<List<string>>? TagCandidateGroups { get; set; }

        [JsonProperty("tagOperator", Order = 2)]
        public string? TagOperator { get; set; }

        [JsonProperty("comparison", Order = 3)]
        public string? Comparison { get; set; }

        [JsonProperty("amount", Order = 4)]
        public int Amount { get; set; }
    }

    private sealed class DayConditionWire
    {
        [JsonProperty("day", Order = 0)]
        public int Day { get; set; }

        [JsonProperty("comparison", Order = 1)]
        public string? Comparison { get; set; }
    }

    private sealed class RewardFilterWire
    {
        [JsonProperty("cardType", Order = 0)]
        public int CardType { get; set; }

        [JsonProperty("quantity", Order = 1)]
        public int? Quantity { get; set; }

        [JsonProperty("fromAnyHero", Order = 2)]
        public bool FromAnyHero { get; set; }

        [JsonProperty("sizes", Order = 3)]
        public List<int>? Sizes { get; set; }

        [JsonProperty("tiers", Order = 4)]
        public List<int>? Tiers { get; set; }

        [JsonProperty("tags", Order = 5)]
        public List<int>? Tags { get; set; }

        [JsonProperty("keywords", Order = 6)]
        public List<int>? Keywords { get; set; }

        [JsonProperty("filterSummary", Order = 7)]
        public string? FilterSummary { get; set; }

        [JsonProperty("excludedTags", Order = 8)]
        public List<int>? ExcludedTags { get; set; }

        [JsonProperty("excludedKeywords", Order = 9)]
        public List<int>? ExcludedKeywords { get; set; }

        [JsonProperty("usesDayTierTable", Order = 10)]
        public bool UsesDayTierTable { get; set; }

        [JsonProperty("usesDayTierDistribution", Order = 11)]
        public bool UsesDayTierDistribution { get; set; }
    }
}
