#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BazaarPlusPlus.Core.Runtime;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus;

internal static class CardsJsonCache
{
    private static readonly string[] TierOrder =
    [
        "Bronze",
        "Silver",
        "Gold",
        "Diamond",
        "Legendary",
    ];

    private static readonly object SyncRoot = new();
    private static CardsJsonSnapshot _snapshot = CardsJsonSnapshot.Empty;
    private static string? _loadedPath;

    internal static bool Warm()
    {
        return TryGetSnapshot(out _);
    }

    internal static void ResetForTests()
    {
        lock (SyncRoot)
        {
            _snapshot = CardsJsonSnapshot.Empty;
            _loadedPath = null;
        }
    }

    internal static bool TryGetSnapshot(out CardsJsonSnapshot snapshot)
    {
        snapshot = CardsJsonSnapshot.Empty;
        var path = BppRuntimeHost.Paths.CardsJsonPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
        {
            snapshot = _snapshot;
            return true;
        }

        lock (SyncRoot)
        {
            if (!string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _snapshot = LoadSnapshot(path);
                    _loadedPath = path;
                }
                catch (Exception ex)
                {
                    BppLog.Error("CardsJsonCache", $"Failed to load cards.json from '{path}'", ex);
                    return false;
                }
            }

            snapshot = _snapshot;
            return true;
        }
    }

    private static CardsJsonSnapshot LoadSnapshot(string path)
    {
        var root = JObject.Parse(File.ReadAllText(path));
        var versionNode =
            root["5.0.0"] as JArray ?? root.Properties().FirstOrDefault()?.Value as JArray;
        if (versionNode == null)
            return CardsJsonSnapshot.Empty;

        var templateIds = new HashSet<Guid>();
        var attributesByTemplateId = new Dictionary<Guid, CardAttributesSnapshot>();
        foreach (var token in versionNode.OfType<JObject>())
        {
            var idText = token.Value<string>("Id");
            if (!Guid.TryParse(idText, out var templateId))
                continue;

            templateIds.Add(templateId);

            var tiers = new Dictionary<string, IReadOnlyDictionary<string, int>>(
                StringComparer.Ordinal
            );
            var tiersObject = token["Tiers"] as JObject;
            if (tiersObject != null)
            {
                foreach (var property in tiersObject.Properties())
                {
                    var normalizedTier = NormalizeTier(property.Name);
                    if (normalizedTier == null)
                        continue;

                    var attributesObject = property.Value["Attributes"] as JObject;
                    tiers[normalizedTier] = ParseAttributes(attributesObject);
                }
            }

            attributesByTemplateId[templateId] = new CardAttributesSnapshot(tiers);
        }

        return new CardsJsonSnapshot(templateIds, attributesByTemplateId);
    }

    private static IReadOnlyDictionary<string, int> ParseAttributes(JObject? attributesObject)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (attributesObject == null)
            return result;

        foreach (var property in attributesObject.Properties())
        {
            if (property.Value.Type != JTokenType.Integer)
                continue;

            result[property.Name] = property.Value.Value<int>();
        }

        return result;
    }

    internal static string? NormalizeTier(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
            return null;

        return TierOrder.FirstOrDefault(candidate =>
            string.Equals(candidate, tier.Trim(), StringComparison.OrdinalIgnoreCase)
        );
    }

    internal static int CompareTier(string left, string right)
    {
        return Array.IndexOf(TierOrder, left) - Array.IndexOf(TierOrder, right);
    }

    internal static IEnumerable<string> GetTierOrder()
    {
        return TierOrder;
    }
}

internal sealed class CardsJsonSnapshot
{
    internal static readonly CardsJsonSnapshot Empty = new(
        new HashSet<Guid>(),
        new Dictionary<Guid, CardAttributesSnapshot>()
    );

    internal CardsJsonSnapshot(
        HashSet<Guid> templateIds,
        IReadOnlyDictionary<Guid, CardAttributesSnapshot> attributesByTemplateId
    )
    {
        TemplateIds = templateIds;
        AttributesByTemplateId = attributesByTemplateId;
    }

    public HashSet<Guid> TemplateIds { get; }

    public IReadOnlyDictionary<Guid, CardAttributesSnapshot> AttributesByTemplateId { get; }
}

internal sealed class CardAttributesSnapshot
{
    internal CardAttributesSnapshot(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> attributesByTier
    )
    {
        AttributesByTier = attributesByTier;
    }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> AttributesByTier { get; }
}
