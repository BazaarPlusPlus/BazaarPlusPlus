using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.TestSupport.EncounterPreview;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EncounterPreview.Tests;

/// <summary>
/// Core's card reader over static data's JSON as the Android host reads it, held to the shared
/// card table the desktop's game-serialized templates are held to as well.
/// </summary>
public sealed class EncounterPreviewCardReaderTests
{
    // Static data already names its enum values.
    private static readonly EncounterPreviewCardReader Reader = new(
        (_, raw) => raw,
        name => Enum.TryParse<CardHero>(name, out var hero) ? hero : null
    );

    [Theory]
    [MemberData(
        nameof(EncounterPreviewCardCases.Names),
        MemberType = typeof(EncounterPreviewCardCases)
    )]
    public void Reads_each_static_data_card_as_the_table_expects(string name)
    {
        var card = EncounterPreviewCardCases.Card(name);

        var template = Reader.ReadTemplate(card);

        Assert.NotNull(template);
        var plan =
            template.Kind == EncounterPreviewTemplateKind.Event ? Reader.ReadEvent(card) : null;
        var actual = EncounterPreviewCardCases.Describe(template, plan);
        var expected = EncounterPreviewCardCases.Expected(name);
        Assert.True(JToken.DeepEquals(expected, actual), $"{name}: {actual}");
    }

    [Fact]
    public void A_card_without_an_id_has_no_plans()
    {
        var card = JObject.Parse("""{ "Type": "EventEncounter", "Tags": ["Merchant"] }""");

        Assert.Null(Reader.ReadTemplate(card));
        Assert.Null(Reader.ReadEvent(card));
    }
}
