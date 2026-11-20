using System.Reflection;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Infra.Serialization;
using BazaarPlusPlus.TestSupport.EncounterPreview;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EncounterPreviewCardRoundTripTests
{
    // The game's serializer scans loaded assemblies on first use. This test's types must resolve in any xUnit order.
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
