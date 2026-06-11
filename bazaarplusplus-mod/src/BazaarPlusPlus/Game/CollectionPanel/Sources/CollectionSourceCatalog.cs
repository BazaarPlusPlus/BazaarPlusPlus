#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal static class CollectionSourceCatalog
{
    private const int ExpectedSchemaVersion = 4;
    private const string LogComponent = "CollectionSourceCatalog";
    private const string ResourceSuffix = "collection-sources.json";

    private static readonly object SyncRoot = new();
    private static bool _loaded;
    private static IReadOnlyList<CollectionSourceEntry> _entries =
        Array.Empty<CollectionSourceEntry>();
    private static IReadOnlyDictionary<Guid, CollectionSourceEntry> _bySourceTemplateId =
        new Dictionary<Guid, CollectionSourceEntry>();
    private static IReadOnlyDictionary<string, CollectionSourceEntry> _bySourceKey = new Dictionary<
        string,
        CollectionSourceEntry
    >(StringComparer.Ordinal);

    public static IReadOnlyList<CollectionSourceEntry> Entries
    {
        get
        {
            EnsureLoaded();
            return _entries;
        }
    }

    public static bool TryGet(Guid sourceTemplateId, out CollectionSourceEntry? entry)
    {
        EnsureLoaded();
        return _bySourceTemplateId.TryGetValue(sourceTemplateId, out entry);
    }

    public static bool TryGetBySourceKey(string sourceKey, out CollectionSourceEntry? entry)
    {
        EnsureLoaded();
        return _bySourceKey.TryGetValue(sourceKey, out entry);
    }

    public static IEnumerable<CollectionSourceEntry> For(
        CollectionSourceKind kind,
        EHero? selectedHero
    )
    {
        EnsureLoaded();
        return VisibleEntries(_entries, kind, selectedHero);
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        lock (SyncRoot)
        {
            if (_loaded)
                return;

            try
            {
                var json = ReadEmbeddedJson(ResourceSuffix);
                if (!string.IsNullOrEmpty(json))
                {
                    var entries = Build(json!);
                    _entries = entries;
                    _bySourceTemplateId = BuildSourceTemplateIndex(entries);
                    _bySourceKey = BuildSourceKeyIndex(entries);
                    BppLog.Info(
                        LogComponent,
                        $"Loaded source catalog entries={entries.Count} sourceTemplateIds={_bySourceTemplateId.Count}"
                    );
                }
            }
            catch (Exception ex)
            {
                BppLog.Error(LogComponent, "Failed to load source catalog", ex);
            }
            finally
            {
                _loaded = true;
            }
        }
    }

    internal static IReadOnlyList<CollectionSourceEntry> Build(string json)
    {
        var dto = JsonConvert.DeserializeObject<CollectionSourceCatalogDto>(json);
        if (dto == null)
            throw new InvalidOperationException("Collection source catalog JSON is empty.");
        if (dto.SchemaVersion != ExpectedSchemaVersion)
            throw new InvalidOperationException(
                $"Collection source catalog schemaVersion must be {ExpectedSchemaVersion}; got {dto.SchemaVersion}."
            );
        if (dto.Entries == null)
            throw new InvalidOperationException("Collection source catalog entries are missing.");

        if (dto.Groups == null || dto.Groups.Count == 0)
            throw new InvalidOperationException("Collection source catalog groups are missing.");
        var groupOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var g = 0; g < dto.Groups.Count; g++)
        {
            var groupId = RequiredText(dto.Groups[g], $"groups[{g}]");
            if (groupOrder.ContainsKey(groupId))
                throw new InvalidOperationException($"Duplicate group id '{groupId}' in groups.");
            groupOrder[groupId] = g;
        }

        var candidates = new List<EntryBuildCandidate>(dto.Entries.Count);
        var usedSourceTemplateIds = new Dictionary<Guid, string>();
        for (var i = 0; i < dto.Entries.Count; i++)
        {
            var entry = dto.Entries[i];
            if (entry == null)
                throw new InvalidOperationException($"Collection source entry {i} is null.");

            var name = RequiredText(entry.Name, $"entries[{i}].name");
            var kind = ParseEnum<CollectionSourceKind>(entry.Kind, $"entries[{i}].kind");
            var group = RequiredText(entry.Group, $"entries[{i}].group");
            if (!groupOrder.TryGetValue(group, out var groupDisplayIndex))
                throw new InvalidOperationException(
                    $"entries[{i}].group '{group}' is not declared in groups."
                );
            if (entry.Order is not int order)
                throw new InvalidOperationException($"entries[{i}].order is required.");
            var availableHeroes = ParseEnumList<EHero>(
                entry.AvailableHeroes,
                $"entries[{i}].availableHeroes"
            );
            var description = entry.Description ?? string.Empty;
            var portraitTemplateId = ParseGuid(
                entry.PortraitTemplateId,
                $"entries[{i}].portraitTemplateId"
            );
            var sourceTemplateIds = ParseGuidList(
                entry.SourceTemplateIds,
                $"entries[{i}].sourceTemplateIds"
            );
            if (!sourceTemplateIds.Contains(portraitTemplateId))
                throw new InvalidOperationException(
                    $"entries[{i}].sourceTemplateIds must include portraitTemplateId {portraitTemplateId}."
                );
            foreach (var sourceTemplateId in sourceTemplateIds)
            {
                if (usedSourceTemplateIds.TryGetValue(sourceTemplateId, out var existing))
                    throw new InvalidOperationException(
                        $"entries[{i}].sourceTemplateIds contains duplicate source template id {sourceTemplateId}; already used by {existing}."
                    );
                usedSourceTemplateIds[sourceTemplateId] = $"entries[{i}]";
            }
            var offerSegments = BuildOfferSegments(
                entry.OfferSegments,
                $"entries[{i}].offerSegments"
            );

            candidates.Add(
                new EntryBuildCandidate(
                    BuildBaseSourceKey(kind, name, availableHeroes),
                    kind,
                    name,
                    availableHeroes,
                    description,
                    portraitTemplateId,
                    sourceTemplateIds,
                    offerSegments,
                    group,
                    order,
                    groupDisplayIndex
                )
            );
        }

        var baseKeyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            baseKeyCounts.TryGetValue(candidate.BaseSourceKey, out var count);
            baseKeyCounts[candidate.BaseSourceKey] = count + 1;
        }

        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CollectionSourceEntry>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var sourceKey =
                baseKeyCounts[candidate.BaseSourceKey] == 1
                    ? candidate.BaseSourceKey
                    : $"{candidate.BaseSourceKey}:{BuildTemplateIdsFingerprint(candidate.SourceTemplateIds)}";
            if (!usedKeys.Add(sourceKey))
                throw new InvalidOperationException(
                    $"Duplicate collection source key: {sourceKey}"
                );

            result.Add(
                new CollectionSourceEntry(
                    sourceKey,
                    candidate.Kind,
                    candidate.Name,
                    candidate.AvailableHeroes,
                    candidate.Description,
                    candidate.PortraitTemplateId,
                    candidate.SourceTemplateIds,
                    candidate.OfferSegments,
                    candidate.Group,
                    candidate.Order,
                    candidate.GroupDisplayIndex
                )
            );
        }

        return result;
    }

    internal static IEnumerable<CollectionSourceEntry> VisibleEntries(
        IReadOnlyList<CollectionSourceEntry> entries,
        CollectionSourceKind kind,
        EHero? selectedHero
    )
    {
        foreach (var entry in entries)
        {
            if (entry.Kind != kind)
                continue;
            if (selectedHero.HasValue && !entry.AppliesToHero(selectedHero.Value))
                continue;
            yield return entry;
        }
    }

    private static IReadOnlyList<CollectionSourceOfferSegment> BuildOfferSegments(
        IReadOnlyList<CollectionSourceOfferSegmentDto>? dtos,
        string path
    )
    {
        if (dtos == null || dtos.Count == 0)
            throw new InvalidOperationException($"{path} must contain at least one segment.");

        var segments = new List<CollectionSourceOfferSegment>(dtos.Count);
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < dtos.Count; i++)
        {
            var dto = dtos[i];
            if (dto == null)
                throw new InvalidOperationException($"{path}[{i}] is null.");
            var segmentPath = $"{path}[{i}]";
            var key = RequiredText(dto.Key, $"{segmentPath}.key");
            if (!usedKeys.Add(key))
                throw new InvalidOperationException($"{segmentPath}.key '{key}' is duplicated.");
            var kind = ParseEnum<CollectionSourceOfferSegmentKind>(dto.Kind, $"{segmentPath}.kind");
            var rule = BuildOfferRule(dto.Rule, $"{segmentPath}.rule");
            segments.Add(
                new CollectionSourceOfferSegment(
                    key,
                    kind,
                    dto.RarityLabel?.Trim() ?? string.Empty,
                    rule
                )
            );
        }

        var hasPinnedTier = segments.Any(segment => segment.Rule.StartingTier != null);
        var hasUnpinnedTier = segments.Any(segment => segment.Rule.StartingTier == null);
        if (hasPinnedTier && hasUnpinnedTier)
            throw new InvalidOperationException(
                $"{path} cannot mix startingTier and non-startingTier segments in schema v4."
            );

        return segments;
    }

    private static CollectionSourceOfferRule BuildOfferRule(
        CollectionSourceOfferRuleDto? dto,
        string path
    )
    {
        if (dto == null)
            throw new InvalidOperationException($"{path} is missing.");

        var heroMode = ParseEnum<CollectionSourceHeroMode>(dto.HeroMode, $"{path}.heroMode");
        EHero? hero = null;
        if (!string.IsNullOrWhiteSpace(dto.Hero))
            hero = ParseEnum<EHero>(dto.Hero, $"{path}.hero");

        if (heroMode == CollectionSourceHeroMode.FixedHero && !hero.HasValue)
            throw new InvalidOperationException($"{path}.hero is required for FixedHero rules.");
        if (heroMode == CollectionSourceHeroMode.NeutralOnly && hero.HasValue)
            throw new InvalidOperationException($"{path}.hero is invalid for NeutralOnly rules.");

        CollectionSourceStartingTierRule? startingTier = null;
        if (dto.StartingTier != null)
        {
            startingTier = new CollectionSourceStartingTierRule(
                ParseEnum<CollectionSourceStartingTierMode>(
                    dto.StartingTier.Mode,
                    $"{path}.startingTier.mode"
                ),
                ParseEnum<ETier>(dto.StartingTier.Tier, $"{path}.startingTier.tier")
            );
        }

        var hiddenTags = MergeHiddenTags(
            ParseEnumList<EHiddenTag>(dto.HiddenTagsAny, $"{path}.hiddenTagsAny"),
            CollectionHiddenTagGroups.Expand(dto.HiddenTagGroupsAny, $"{path}.hiddenTagGroupsAny")
        );

        return new CollectionSourceOfferRule(
            heroMode,
            hero,
            startingTier,
            ParseEnumList<ECardSize>(dto.SizesAny, $"{path}.sizesAny"),
            ParseEnumList<ECardTag>(dto.TagsAny, $"{path}.tagsAny"),
            ParseEnumList<ECardTag>(dto.TagsNone, $"{path}.tagsNone"),
            hiddenTags,
            dto.EnchantableOnly,
            ParseEnumList<EEnchantmentType>(dto.EnchantmentTypesAny, $"{path}.enchantmentTypesAny"),
            ParseEnumList<ECardTag>(dto.EnchantmentTagsAny, $"{path}.enchantmentTagsAny"),
            ParseEnumList<EHiddenTag>(
                dto.EnchantmentHiddenTagsAny,
                $"{path}.enchantmentHiddenTagsAny"
            )
        );
    }

    private static IReadOnlyList<EHiddenTag> MergeHiddenTags(
        IReadOnlyList<EHiddenTag> explicitTags,
        IReadOnlyList<EHiddenTag> groupTags
    )
    {
        if (explicitTags.Count == 0)
            return groupTags;
        if (groupTags.Count == 0)
            return explicitTags;

        var result = new List<EHiddenTag>(explicitTags.Count + groupTags.Count);
        var used = new HashSet<EHiddenTag>();
        foreach (var tag in explicitTags)
        {
            if (used.Add(tag))
                result.Add(tag);
        }
        foreach (var tag in groupTags)
        {
            if (used.Add(tag))
                result.Add(tag);
        }
        return result;
    }

    private static IReadOnlyDictionary<Guid, CollectionSourceEntry> BuildSourceTemplateIndex(
        IReadOnlyList<CollectionSourceEntry> entries
    )
    {
        var map = new Dictionary<Guid, CollectionSourceEntry>();
        foreach (var entry in entries)
        foreach (var id in entry.SourceTemplateIds)
            map[id] = entry;
        return map;
    }

    private static IReadOnlyDictionary<string, CollectionSourceEntry> BuildSourceKeyIndex(
        IReadOnlyList<CollectionSourceEntry> entries
    )
    {
        var map = new Dictionary<string, CollectionSourceEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
            map[entry.SourceKey] = entry;
        return map;
    }

    private static string BuildBaseSourceKey(
        CollectionSourceKind kind,
        string name,
        IReadOnlyList<EHero> heroes
    )
    {
        var heroKey =
            heroes.Count == 0
                ? "global"
                : string.Join(
                    "+",
                    heroes
                        .Select(hero => hero.ToString())
                        .OrderBy(value => value, StringComparer.Ordinal)
                );
        return string.Join(":", kind.ToString().ToLowerInvariant(), Slug(name), Slug(heroKey));
    }

    private static string BuildTemplateIdsFingerprint(IReadOnlyList<Guid> templateIds) =>
        string.Join(
            "-",
            templateIds.OrderBy(id => id).Select(id => id.ToString("N").Substring(0, 12))
        );

    private static string RequiredText(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{path} is required.");
        return value!.Trim();
    }

    private static TEnum ParseEnum<TEnum>(string? value, string path)
        where TEnum : struct
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{path} is required.");
        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
            throw new InvalidOperationException($"{path} has invalid value '{value}'.");
        return parsed;
    }

    private static IReadOnlyList<TEnum> ParseEnumList<TEnum>(
        IReadOnlyList<string>? values,
        string path
    )
        where TEnum : struct
    {
        if (values == null || values.Count == 0)
            return Array.Empty<TEnum>();

        var result = new List<TEnum>(values.Count);
        foreach (var value in values)
            result.Add(ParseEnum<TEnum>(value, path));
        return result;
    }

    private static Guid ParseGuid(string? value, string path)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
            throw new InvalidOperationException($"{path} must be a non-empty GUID.");
        return id;
    }

    private static IReadOnlyList<Guid> ParseGuidList(IReadOnlyList<string>? values, string path)
    {
        if (values == null || values.Count == 0)
            throw new InvalidOperationException($"{path} must have at least one GUID.");

        var result = new List<Guid>(values.Count);
        foreach (var value in values)
            result.Add(ParseGuid(value, path));
        return result;
    }

    private static string Slug(string value)
    {
        var chars = new List<char>(value.Length);
        var lastWasSeparator = false;
        foreach (var raw in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(raw))
            {
                chars.Add(raw);
                lastWasSeparator = false;
                continue;
            }

            if (!lastWasSeparator && chars.Count > 0)
            {
                chars.Add('-');
                lastWasSeparator = true;
            }
        }

        if (chars.Count > 0 && chars[chars.Count - 1] == '-')
            chars.RemoveAt(chars.Count - 1);
        return chars.Count == 0 ? "unknown" : new string(chars.ToArray());
    }

    private static string? ReadEmbeddedJson(string suffix)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName == null)
        {
            BppLog.Warn(LogComponent, $"Embedded catalog resource not found suffix={suffix}");
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class EntryBuildCandidate
    {
        public EntryBuildCandidate(
            string baseSourceKey,
            CollectionSourceKind kind,
            string name,
            IReadOnlyList<EHero> availableHeroes,
            string description,
            Guid portraitTemplateId,
            IReadOnlyList<Guid> sourceTemplateIds,
            IReadOnlyList<CollectionSourceOfferSegment> offerSegments,
            string group,
            int order,
            int groupDisplayIndex
        )
        {
            BaseSourceKey = baseSourceKey;
            Kind = kind;
            Name = name;
            AvailableHeroes = availableHeroes;
            Description = description;
            PortraitTemplateId = portraitTemplateId;
            SourceTemplateIds = sourceTemplateIds;
            OfferSegments = offerSegments;
            Group = group;
            Order = order;
            GroupDisplayIndex = groupDisplayIndex;
        }

        public string BaseSourceKey { get; }

        public CollectionSourceKind Kind { get; }

        public string Name { get; }

        public IReadOnlyList<EHero> AvailableHeroes { get; }

        public string Description { get; }

        public Guid PortraitTemplateId { get; }

        public IReadOnlyList<Guid> SourceTemplateIds { get; }

        public IReadOnlyList<CollectionSourceOfferSegment> OfferSegments { get; }

        public string Group { get; }

        public int Order { get; }

        public int GroupDisplayIndex { get; }
    }
}
