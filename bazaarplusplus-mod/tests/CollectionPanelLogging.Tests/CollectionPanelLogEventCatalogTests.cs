#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace CollectionPanelLogging.Tests;

public sealed class CollectionPanelLogEventCatalogTests
{
    [Fact]
    public void Events_match_the_locked_D01_D27_manifest()
    {
        var actual = Definitions().ToDictionary(x => x.EventId, Describe, StringComparer.Ordinal);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["collection_panel.mount.failed"] = "reason_code:Low:None",
            ["collection_panel.open.failed"] = "reason_code:Low:None",
            ["collection_panel.open.skipped"] = "reason_code:Low:None",
            ["collection_panel.selection.resolved"] =
                "source:Low:None|hero:Low:None|day:Low:None|encounter_id:High:None",
            ["collection_panel.selection.degraded"] = "probe:Low:None|reason_code:Low:None",
            ["collection_panel.selection.recovered"] = "probe:Low:None",
            ["collection_panel.load.completed"] =
                "phase:Low:None|outcome:Low:None|reason_code:Low:None|duration_ms:High:None|catalog_acquire_duration_ms:High:None|catalog_duration_ms:High:None|filter_duration_ms:High:None|refresh_duration_ms:High:None|catalog_cache_hit:Low:None|source_template_count:High:None|accepted_count:High:None|rejected_count:High:None|catalog_card_count:High:None|visible_card_count:High:None",
            ["collection_panel.cleanup.degraded"] = "reason_code:Low:None",
            ["collection_panel.card.bind_degraded"] =
                "stage:Low:None|template_id:High:None|reason_code:Low:None",
            ["collection_panel.dock_button.setup_failed"] =
                "placement:Low:None|reason_code:Low:None",
            ["collection_panel.dock_layout.degraded"] = "reason_code:Low:None|blocker:High:None",
            ["collection_panel.dock_layout.recovered"] = "reason_code:Low:None|blocker:High:None",
            ["collection_panel.card.display_failed"] = "stage:Low:None|template_id:High:None",
            ["collection_panel.grid.performance_observed"] =
                "phase:Low:None|first_index:High:None|last_index:High:None|window_count:High:None|visible_count:High:None|shelf_count:High:None|attempt_count:High:None|bound_count:High:None|failed_bind_count:High:None|bind_duration_ms:High:None|elapsed_ms:High:None|faulted_count:High:None|canceled_count:High:None",
            ["collection_panel.card_art.degraded"] =
                "reason_code:Low:None|status:Low:None|art_key:High:None",
            ["collection_panel.tier_tooltip.degraded"] = "tier_field:Low:None|reason_code:Low:None",
            ["collection_panel.hero_portrait.degraded"] = "hero:Low:None|reason_code:Low:None",
            ["collection_panel.hero_portrait.fallback_observed"] =
                "hero:Low:None|reason_code:Low:None",
            ["collection_panel.encounter_portrait.degraded"] =
                "template_id:High:None|reason_code:Low:None|art_key:High:None",
            ["collection_panel.keyword_icon.degraded"] = "reason_code:Low:None|icon_name:High:None",
            ["collection_panel.tag_typography.degraded"] = "reason_code:Low:None",
            ["collection_panel.cache.cleanup_failed"] =
                "cache:Low:None|stage:Low:None|art_key:High:None",
            ["collection_panel.hover.invoke_failed"] = "operation:Low:None",
            ["collection_panel.hero_preference.degraded"] = "reason_code:Low:None|hero:Low:None",
            ["collection_panel.hero_preference.scope_degraded"] = "reason_code:Low:None",
            ["collection_panel.source_catalog.loaded"] =
                "entry_count:High:None|source_template_count:High:None",
            ["collection_panel.source_catalog.load_failed"] =
                "reason_code:Low:None|resource_suffix:Low:None",
            ["collection_panel.catalog.build_deferred"] = "reason_code:Low:None",
            ["collection_panel.catalog.degraded"] = "reason_code:Low:None",
            ["collection_panel.catalog.ready"] =
                "accepted_count:High:None|rejected_count:High:None|source_template_count:High:None",
            ["collection_panel.catalog.recovered"] =
                "accepted_count:High:None|rejected_count:High:None|source_template_count:High:None",
            ["collection_panel.catalog.invalidated"] = "reason_code:Low:None",
        };

        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected)
            Assert.Equal(pair.Value, actual[pair.Key]);
        Assert.All(
            Definitions(),
            definition => Assert.Same(BppLogFeatureScope.CollectionPanel, definition.Scope)
        );
        Assert.NotNull(
            typeof(CollectionPanelLogEvents).GetCustomAttribute<BppLogEventSourceAttribute>()
        );
        var validation = BppLogEventCatalog.FromDefinitions(Definitions().ToArray()).Validate();
        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Violations.Select(violation => violation.ToString()))
        );
    }

    [Fact]
    public void Warning_storm_keys_are_low_cardinality_and_exclude_untrusted_values()
    {
        AssertStorm(CollectionPanelLogEvents.SelectionDegraded, "probe", "reason_code");
        AssertStorm(CollectionPanelLogEvents.CleanupDegraded, "reason_code");
        AssertStorm(CollectionPanelLogEvents.CardBindDegraded, "stage", "reason_code");
        AssertStorm(CollectionPanelLogEvents.DockLayoutDegraded, "reason_code");
        AssertStorm(CollectionPanelLogEvents.CardArtDegraded, "reason_code");
        AssertStorm(CollectionPanelLogEvents.TierTooltipDegraded, "tier_field", "reason_code");
        AssertStorm(CollectionPanelLogEvents.HeroPortraitDegraded, "reason_code");
        AssertStorm(CollectionPanelLogEvents.EncounterPortraitDegraded, "reason_code");
        AssertStorm(CollectionPanelLogEvents.KeywordIconDegraded, "reason_code");
        AssertStorm(CollectionPanelLogEvents.TagTypographyDegraded, "reason_code");
        AssertStorm(CollectionPanelLogEvents.HeroPreferenceDegraded, "reason_code");
        AssertStorm(CollectionPanelLogEvents.HeroPreferenceScopeDegraded, "reason_code");
        AssertStorm(CollectionPanelLogEvents.CatalogDegraded, "reason_code");
    }

    [Fact]
    public void Art_keys_and_layout_blockers_are_bounded_escaped_untrusted_text()
    {
        var renderer = new BppLogEventRenderer();
        var hostile = "root\r\nnext\t" + new string('x', 100_000);

        var art = renderer.Render(
            CollectionPanelLogEvents.CardArtDegraded,
            CollectionPanelLogEvents.CardArtDegradedReasonCode.Bind(
                CollectionPanelLogReasonCode.AddressablesLoadFailed
            ),
            CollectionPanelLogEvents.CardArtDegradedStatus.Bind(
                CollectionCardArtStatus.ArtUnavailable
            ),
            CollectionPanelLogEvents.CardArtDegradedArtKey.Bind(hostile)
        );
        var layout = renderer.Render(
            CollectionPanelLogEvents.DockLayoutDegraded,
            CollectionPanelLogEvents.DockLayoutDegradedReasonCode.Bind(
                CollectionPanelLogReasonCode.PlacementBlocked
            ),
            CollectionPanelLogEvents.DockLayoutDegradedBlocker.Bind(hostile)
        );

        Assert.DoesNotContain('\r', art);
        Assert.DoesNotContain('\n', art);
        Assert.Contains("root\\r\\nnext\\t", art);
        Assert.Contains("field_truncated=true", art);
        Assert.True(art.Length <= BppLogEventRenderer.RecordCharacterBudget);
        Assert.DoesNotContain('\r', layout);
        Assert.DoesNotContain('\n', layout);
        Assert.Contains("field_truncated=true", layout);
        Assert.True(layout.Length <= BppLogEventRenderer.RecordCharacterBudget);
    }

    private static void AssertStorm(BppLogEventDefinition definition, params string[] fields)
    {
        var policy = Assert.IsType<BppLogStormPolicy>(definition.StormPolicy);
        Assert.Equal(fields, policy.KeyFields.Select(x => x.Name));
        Assert.All(
            policy.KeyFields,
            field =>
            {
                Assert.Equal(BppLogCardinality.Low, field.Cardinality);
                Assert.Equal(BppLogCorrelationPolicy.None, field.Correlation);
            }
        );
    }

    private static IEnumerable<BppLogEventDefinition> Definitions() =>
        typeof(CollectionPanelLogEvents)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
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
