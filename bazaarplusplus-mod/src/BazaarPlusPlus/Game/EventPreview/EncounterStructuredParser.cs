#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Spawning;
using BazaarGameShared.Infra.Serialization;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.EventPreview;

/// <summary>
/// The desktop's route into Core's card reader: a typed template serialized with the settings the
/// game reads static data with (<c>JsonGameDataManager</c>), so its JSON carries the same
/// <c>$type</c> discriminators and enum names as the static-data rows the Android host reads.
/// </summary>
internal static class EncounterStructuredParser
{
    // Built once: the game's converter reflects over every discriminated type when constructed.
    private static readonly Lazy<JsonSerializer> GameSerializer = new(() =>
    {
        var serializer = JsonSerializer.Create(new BazaarJsonSerializerSettings());
        CompatibleCardValueConverter.Install(serializer);
        return serializer;
    });

    public static readonly EncounterPreviewCardReader Reader = new(NormalizeChoiceEnum, Hero);

    internal static JToken? TryPrepareToken(object? source)
    {
        if (source == null)
            return null;
        try
        {
            return source switch
            {
                JToken token => token,
                string json when !string.IsNullOrWhiteSpace(json) => JToken.Parse(json),
                _ => JToken.FromObject(source, GameSerializer.Value),
            };
        }
        // The game's converter indexes its discriminator table without a check, so a convertible
        // type it has no discriminator for throws KeyNotFoundException.
        catch (Exception ex)
            when (ex is JsonException
                || ex is InvalidOperationException
                || ex is KeyNotFoundException
            )
        {
            return null;
        }
    }

    internal static string? NormalizeChoiceEnum(string kind, string? raw) =>
        kind switch
        {
            "SelectionMethod" => Name<ESpawnSelectionMethod>(raw),
            "Comparison" => Name<EComparisonOperator>(raw),
            "ListComparison" => Name<EListComparisonOperator>(raw),
            "HiddenTag" => Name<EHiddenTag>(raw),
            "Tag" => Name<ECardTag>(raw),
            "Tier" => Name<ETier>(raw),
            "Size" => Name<ECardSize>(raw),
            "CardType" => Name<ECardType>(raw),
            _ => null,
        };

    private static string? Name<T>(string? raw)
        where T : struct, Enum =>
        Enum.TryParse<T>(raw, true, out var value) ? value.ToString() : null;

    private static CardHero? Hero(string name) =>
        Enum.TryParse<EHero>(name, out var native)
        && DesktopCardFacets.Hero.TryToFacet(native, out var hero)
            ? hero
            : null;
}
