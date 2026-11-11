using System.Reflection;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Infra.Serialization;
using BazaarPlusPlus.TestSupport.EncounterPreview;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EncounterTooltip.Tests;

/// <summary>
/// The desktop's route into Core's card reader: each static-data card of the shared table, read
/// into its typed template as the game reads static data, then written back with the game's
/// serializer, reads to the same plans as the card's own JSON does on Android.
/// </summary>
public sealed class EncounterPreviewCardRoundTripTests
{
    /// <summary>
    /// The game's serializer scans the types of every assembly loaded when it is first used, and
    /// xUnit picks the order of test classes. When a class that loads the game runtime runs first,
    /// the scan reads this assembly's types, so they must always resolve.
    /// </summary>
    [Fact]
    public void The_game_runtime_types_resolve_for_the_serializer_scan()
    {
        Assert.NotEmpty(Assembly.Load("TheBazaarRuntime").GetTypes());
    }

    [Theory]
    [MemberData(
        nameof(EncounterPreviewCardCases.Names),
        MemberType = typeof(EncounterPreviewCardCases)
    )]
    public void A_game_serialized_template_reads_as_its_static_data_does(string name)
    {
        var typed = JsonConvert.DeserializeObject<ITCard>(
            EncounterPreviewCardCases.Card(name).ToString(),
            new BazaarJsonSerializerSettings()
        );

        var token = Assert.IsType<JObject>(EncounterStructuredParser.TryPrepareToken(typed));
        var template = EncounterStructuredParser.Reader.ReadTemplate(token);

        Assert.NotNull(template);
        var plan =
            template.Kind == EncounterPreviewTemplateKind.Event
                ? EncounterStructuredParser.Reader.ReadEvent(token)
                : null;
        var actual = EncounterPreviewCardCases.Describe(template, plan);
        var expected = EncounterPreviewCardCases.Expected(name);
        Assert.True(JToken.DeepEquals(expected, actual), $"{name}: {actual}");
    }
}
