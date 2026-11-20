#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Spawning;
using BazaarGameShared.Infra.Serialization;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.EncounterPreview;

// The game's serializer preserves the static-data type discriminators and enum names that Core also reads on Android.
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
            return JToken.FromObject(source, GameSerializer.Value);
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
