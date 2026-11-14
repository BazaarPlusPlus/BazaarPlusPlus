#nullable enable
using BazaarPlusPlus.Core.GameState;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.RandomCosmetics;

/// <summary>
/// Where a Random Cosmetic Pool is saved: one PlayerPrefs string per account, hero and
/// category, holding a JSON array of the cosmetic ids left out. The removed predecessor's
/// pools (<c>BPP.RandomCollectiblePool.Selected.*</c>) listed members instead and are not read.
/// </summary>
public static class RandomCosmeticPoolPrefs
{
    public const string KeyPrefix = "BPP.RandomCosmeticPool.v1";
    public const string AnonymousAccountScope = "anonymous";

    /// <summary>
    /// <paramref name="category"/> and <paramref name="heroEnumName"/> are the game's enum
    /// member names; The Dragons' legacy name maps to its canonical id.
    /// </summary>
    public static string BuildKey(string category, string heroEnumName, string? accountScope)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("A category is required.", nameof(category));
        if (string.IsNullOrWhiteSpace(heroEnumName))
            throw new ArgumentException("A hero is required.", nameof(heroEnumName));

        var heroId = TheDragonsHeroId.IsAlias(heroEnumName)
            ? TheDragonsHeroId.CanonicalId
            : heroEnumName.Trim();
        var scope = string.IsNullOrWhiteSpace(accountScope)
            ? AnonymousAccountScope
            : accountScope!.Trim();
        return $"{KeyPrefix}.{Uri.EscapeDataString(category.Trim())}.{Uri.EscapeDataString(heroId)}.{Uri.EscapeDataString(scope)}";
    }

    public static string Serialize(IEnumerable<string> ids) =>
        JsonConvert.SerializeObject(
            ids.Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        );

    /// <summary>The saved ids, or null when the value is blank or not a JSON string array.</summary>
    public static IReadOnlyList<string>? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            var ids = JsonConvert.DeserializeObject<string?[]>(raw!);
            return ids?.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
