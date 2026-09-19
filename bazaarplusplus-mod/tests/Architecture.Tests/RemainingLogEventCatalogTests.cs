#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace Architecture.Tests;

public sealed class RemainingLogEventCatalogTests
{
    [Fact]
    public void Tooltip_events_match_the_locked_batch_E_schemas()
    {
        AssertCatalog(
            typeof(TooltipLogEvents),
            BppLogFeatureScope.Tooltips,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tooltips.level_rewards.rendered_or_skipped"] =
                    "outcome:Low:None|reason_code:Low:None|level:Low:None|content_length:High:None",
                ["tooltips.level_rewards.degraded"] =
                    "outcome:Low:None|reason_code:Low:None|level:Low:None|content_length:High:None",
                ["tooltips.encounter_section.degraded"] = "reason_code:Low:None",
                ["tooltips.encounter_inventory.degraded"] = "reason_code:Low:None",
                ["tooltips.section.degraded"] = "section_id:Low:None|reason_code:Low:None",
                ["tooltips.section_host.degraded"] = "section_id:Low:None|reason_code:Low:None",
                ["tooltips.preview_refresh.degraded"] = "reason_code:Low:None|mode:Low:None",
                ["tooltips.preview_target.resolved_or_skipped"] =
                    "outcome:Low:None|reason_code:Low:None|template_id:High:None|card_instance_id:High:Hash",
                ["tooltips.encounter_probe.degraded"] = "probe:Low:None|reason_code:Low:None",
                ["tooltips.encounter_probe.recovered"] = "probe:Low:None",
                ["tooltips.package_merchant_summary.degraded"] =
                    "reason_code:Low:None|merchant_template_id:High:None",
            }
        );
    }

    [Fact]
    public void Item_enchant_and_Pvp_events_match_the_locked_batch_E_schemas()
    {
        AssertCatalog(
            typeof(ItemEnchantPreviewLogEvents),
            BppLogFeatureScope.ItemEnchantPreview,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["item_enchant_preview.render.degraded"] =
                    "stage:Low:None|reason_code:Low:None|enchantment:Low:None",
                ["item_enchant_preview.encounter_probe.degraded"] =
                    "probe:Low:None|reason_code:Low:None",
                ["item_enchant_preview.encounter_probe.recovered"] = "probe:Low:None",
            }
        );
        AssertCatalog(
            typeof(PvpBattleLogEvents),
            BppLogFeatureScope.PvpBattles,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["pvp_battles.snapshot.degraded"] =
                    "combatant:Low:None|section:Low:None|battle_id:High:Short|reason_code:Low:None",
            }
        );
    }

    [Fact]
    public void Batch_E_warning_storm_keys_are_closed_low_cardinality_fields()
    {
        var definitions = Definitions(typeof(TooltipLogEvents))
            .Concat(Definitions(typeof(ItemEnchantPreviewLogEvents)))
            .Concat(Definitions(typeof(PvpBattleLogEvents)));

        foreach (var definition in definitions.Where(item => item.StormPolicy != null))
        {
            Assert.All(
                definition.StormPolicy!.KeyFields,
                field =>
                {
                    Assert.Equal(BppLogCardinality.Low, field.Cardinality);
                    Assert.Equal(BppLogCorrelationPolicy.None, field.Correlation);
                }
            );
        }
    }

    private static void AssertCatalog(
        Type source,
        BppLogFeatureScope scope,
        IReadOnlyDictionary<string, string> expected
    )
    {
        var definitions = Definitions(source).ToArray();
        var actual = definitions.ToDictionary(
            definition => definition.EventId,
            Describe,
            StringComparer.Ordinal
        );
        Assert.Equal(expected, actual);
        Assert.All(definitions, definition => Assert.Same(scope, definition.Scope));
        Assert.NotNull(source.GetCustomAttribute<BppLogEventSourceAttribute>());
        var validation = BppLogEventCatalog.FromDefinitions(definitions).Validate();
        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Violations.Select(item => item.ToString()))
        );
    }

    private static IEnumerable<BppLogEventDefinition> Definitions(Type source) =>
        source
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!);

    private static string Describe(BppLogEventDefinition definition) =>
        string.Join(
            "|",
            definition.Fields.Select(field =>
                $"{field.Name}:{field.Cardinality}:{field.Correlation}"
            )
        );
}
