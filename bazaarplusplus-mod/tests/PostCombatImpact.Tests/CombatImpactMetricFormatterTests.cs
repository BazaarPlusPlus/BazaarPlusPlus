using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactMetricFormatterTests
{
    private const string BurnIcon = "<sprite name=Burn>";
    private const string CritIcon = "<sprite name=Crit>";
    private const string DamageIcon = "<sprite name=Damage>";
    private const string HealingIcon = "<sprite name=Healing>";

    private static readonly CombatImpactEntity Target = new("target", "Target", "Item", null, 0);

    [Theory]
    [InlineData(950, (int)CombatImpactValueUnit.Milliseconds, false, "0.95s")]
    [InlineData(1000, (int)CombatImpactValueUnit.Milliseconds, false, "1s")]
    [InlineData(1100, (int)CombatImpactValueUnit.Milliseconds, false, "1.10s")]
    [InlineData(950, (int)CombatImpactValueUnit.Milliseconds, true, "0.95s")]
    [InlineData(20, (int)CombatImpactValueUnit.PercentagePoints, false, "20%")]
    [InlineData(-10, (int)CombatImpactValueUnit.Amount, false, "-10")]
    [InlineData(999_999, (int)CombatImpactValueUnit.Amount, false, "999,999")]
    [InlineData(1_000_000, (int)CombatImpactValueUnit.Amount, false, "1M")]
    [InlineData(12_345_678, (int)CombatImpactValueUnit.Amount, false, "12.35M")]
    [InlineData(-12_345_678, (int)CombatImpactValueUnit.Amount, false, "-12.35M")]
    [InlineData(int.MaxValue, (int)CombatImpactValueUnit.Amount, false, "2.15B")]
    [InlineData(int.MinValue, (int)CombatImpactValueUnit.Amount, false, "-2.15B")]
    public void Values_use_product_duration_and_precision_rules(
        int value,
        int unitValue,
        bool chinese,
        string expected
    )
    {
        var formatted = CombatImpactMetricFormatter.Value(
            value,
            (CombatImpactValueUnit)unitValue,
            chinese
        );

        Assert.Equal(expected, formatted);
        Assert.DoesNotContain("≥", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("≈", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("estimated", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("估算", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("*", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Triggered_skill_hides_internal_batch_count_and_keeps_source_breakdown()
    {
        var skill = new CombatImpactEntity("skill", "Wax and Wane", "Skill", null, 0);
        var trigger = new CombatImpactEntity("trigger", "Soul of the\nDistrict", "Item", null, 1);
        var group = new CombatImpactGroup(
            CombatImpactKind.Slow,
            "Slow",
            10,
            10_000,
            CombatImpactValueUnit.Milliseconds,
            CombatImpactCoverage.Exact,
            null,
            0,
            []
        )
        {
            TriggerSources = [new CombatImpactTriggerSource(trigger, 10, 1)],
            TriggerPresentationState = CombatImpactTriggerPresentationState.Complete,
        };
        var source = new CombatImpactSource(skill, 2, 10, [group])
        {
            ObservedActivationBatchCount = 1,
        };

        Assert.Empty(CombatImpactMetricFormatter.CausedSummary(source, chinese: false));
        Assert.Empty(CombatImpactMetricFormatter.CausedSummary(source, chinese: true));
        Assert.Equal(
            "Triggered by: Soul of the District ×1",
            CombatImpactMetricFormatter.TriggerSources(group, chinese: false)
        );
        Assert.Equal(
            "触发来源：Soul of the District ×1",
            CombatImpactMetricFormatter.TriggerSources(group, chinese: true)
        );
    }

    [Fact]
    public void Item_summary_keeps_authoritative_use_count_when_trigger_provenance_exists()
    {
        var source = new CombatImpactSource(Target, 3, 4, []) { ObservedActivationBatchCount = 2 };

        Assert.Equal("3 uses", CombatImpactMetricFormatter.CausedSummary(source, chinese: false));
    }

    [Fact]
    public void Trigger_sources_show_only_known_player_relevant_sources()
    {
        var trigger = new CombatImpactEntity("trigger", "Trigger", "Item", null, 1);
        var group = Group(
            CombatImpactKind.Charge,
            count: 5,
            observedValue: 5,
            unit: CombatImpactValueUnit.Amount
        ) with
        {
            TriggerSources = [new CombatImpactTriggerSource(trigger, 3, 1)],
            NoTriggerEvidenceApplicationCount = 1,
            TriggerFallbackApplicationCount = 1,
            TriggerPresentationState = CombatImpactTriggerPresentationState.PartialBreakdown,
        };

        Assert.Equal(
            "Triggered by: Trigger ×1",
            CombatImpactMetricFormatter.TriggerSources(group, chinese: false)
        );
        Assert.Equal(
            "触发来源：Trigger ×1",
            CombatImpactMetricFormatter.TriggerSources(group, chinese: true)
        );

        var unavailable = group with
        {
            TriggerSources = [],
            TriggerPresentationState = CombatImpactTriggerPresentationState.BreakdownUnavailable,
        };
        Assert.Empty(CombatImpactMetricFormatter.TriggerSources(unavailable, chinese: false));
        Assert.Empty(CombatImpactMetricFormatter.TriggerSources(unavailable, chinese: true));
    }

    [Fact]
    public void Trigger_sources_count_activation_batches_instead_of_target_fan_out()
    {
        var dragRacer = new CombatImpactEntity("drag-racer", "Drag Racer", "Item", null, 1);
        var tambourine = new CombatImpactEntity("tambourine", "Tambourine", "Item", null, 2);
        var group = Group(
            CombatImpactKind.Charge,
            count: 16,
            observedValue: 16_000,
            unit: CombatImpactValueUnit.Milliseconds
        ) with
        {
            TriggerSources =
            [
                new CombatImpactTriggerSource(
                    dragRacer,
                    ApplicationCount: 8,
                    ObservedActivationBatchCount: 2
                ),
                new CombatImpactTriggerSource(
                    tambourine,
                    ApplicationCount: 8,
                    ObservedActivationBatchCount: 2
                ),
            ],
            TriggerPresentationState = CombatImpactTriggerPresentationState.Complete,
        };

        Assert.Equal(
            "Triggered by: Drag Racer ×2 · Tambourine ×2",
            CombatImpactMetricFormatter.TriggerSources(group, chinese: false)
        );
        Assert.Equal("×16 · 16s", CombatImpactMetricFormatter.Group(group, chinese: false));
    }

    [Fact]
    public void Skill_summary_omits_ambiguous_card_stat_use_count_without_executed_provenance()
    {
        var skill = new CombatImpactEntity("skill", "Quick Freeze", "Skill", null, 0);
        var source = new CombatImpactSource(skill, 17, 1, []);

        Assert.Empty(CombatImpactMetricFormatter.CausedSummary(source, chinese: false));
    }

    [Fact]
    public void Critical_counts_stay_in_parentheses_without_a_rate()
    {
        var appliedRegen = new CombatImpactGroup(
            CombatImpactKind.AttributeChange,
            "RegenApplyAmount",
            2,
            12,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.AttributeChange,
                "RegenApplyAmount",
                12,
                CombatImpactValueUnit.Amount,
                CombatImpactAuthoritativeBasis.TotalAmount
            ),
            0,
            []
        )
        {
            CriticalCount = 1,
            CriticalOutcomeCount = 2,
            CriticalObservedValue = 8,
        };
        var damage = Group(
            CombatImpactKind.DirectDamage,
            count: 8,
            observedValue: 640,
            CombatImpactValueUnit.Amount
        ) with
        {
            CriticalCount = 5,
            CriticalOutcomeCount = 8,
            CriticalObservedValue = 640,
        };

        Assert.Equal(
            "×2 (<sprite name=Crit>1) · 12 total",
            CombatImpactMetricFormatter.Group(appliedRegen, false, CritIcon)
        );
        Assert.Equal(
            "×2（<sprite name=Crit>1） · 总计 12",
            CombatImpactMetricFormatter.Group(appliedRegen, true, CritIcon)
        );
        Assert.Equal(
            "×8 (<sprite name=Crit>5) · 640",
            CombatImpactMetricFormatter.Group(damage, false, CritIcon)
        );

        var incoming = new CombatImpactIncomingGroup(
            CombatImpactKind.DirectDamage,
            "Damage",
            8,
            640,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            []
        )
        {
            CriticalCount = 5,
            CriticalOutcomeCount = 8,
        };
        var incomingText = CombatImpactMetricFormatter.IncomingGroup(incoming, false, CritIcon);
        Assert.Equal("×8 (<sprite name=Crit>5) · 640", incomingText);
        Assert.DoesNotContain("%", incomingText, StringComparison.Ordinal);
    }

    [Fact]
    public void Authoritative_totals_use_the_effect_icon_when_available()
    {
        var burn = new CombatImpactGroup(
            CombatImpactKind.Burn,
            "BurnApplyAmount",
            2,
            18,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.Burn,
                "BurnApplyAmount",
                18,
                CombatImpactValueUnit.Amount,
                CombatImpactAuthoritativeBasis.TotalAmount
            ),
            0,
            []
        );

        var formatted = CombatImpactMetricFormatter.Group(
            burn,
            chinese: false,
            effectMarker: BurnIcon
        );

        Assert.Equal("×2 · <sprite name=Burn>18", formatted);
        Assert.DoesNotContain("total", formatted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Attribute_change_values_use_the_effect_icon_without_a_positive_sign()
    {
        var target = new CombatImpactTarget(
            Target,
            3,
            30,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact
        );
        var gained = new CombatImpactGroup(
            CombatImpactKind.AttributeChange,
            "DamageAmount",
            6,
            750,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            null,
            0,
            [target]
        );
        var source = new CombatImpactIncomingSource(
            Target,
            3,
            30,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact
        );
        var incoming = new CombatImpactIncomingGroup(
            CombatImpactKind.AttributeChange,
            "DamageAmount",
            6,
            750,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            [source]
        );
        var lost = gained with { ObservedValue = -750 };

        Assert.Equal(
            "×6 · <sprite name=Damage>750",
            CombatImpactMetricFormatter.Group(gained, chinese: false, effectMarker: DamageIcon)
        );
        Assert.Equal(
            "×3 · <sprite name=Damage>30",
            CombatImpactMetricFormatter.Target(
                gained,
                target,
                chinese: false,
                effectMarker: DamageIcon
            )
        );
        Assert.Equal(
            "×6 · <sprite name=Damage>750",
            CombatImpactMetricFormatter.IncomingGroup(
                incoming,
                chinese: false,
                effectMarker: DamageIcon
            )
        );
        Assert.Equal(
            "×3 · <sprite name=Damage>30",
            CombatImpactMetricFormatter.IncomingSource(
                incoming,
                source,
                chinese: false,
                effectMarker: DamageIcon
            )
        );
        Assert.Equal(
            "×6 · <sprite name=Damage>-750",
            CombatImpactMetricFormatter.Group(lost, chinese: false, effectMarker: DamageIcon)
        );
    }

    [Fact]
    public void Tempo_spent_uses_an_unsigned_consumption_total()
    {
        var spent = new CombatImpactGroup(
            CombatImpactKind.AttributeChange,
            "TempoRemoveAmount",
            2,
            null,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.None,
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.AttributeChange,
                "TempoRemoveAmount",
                7,
                CombatImpactValueUnit.Amount,
                CombatImpactAuthoritativeBasis.TotalAmount
            ),
            0,
            []
        );

        Assert.Equal("×2 · 7 total", CombatImpactMetricFormatter.Group(spent, chinese: false));
        Assert.Equal("×2 · 总计 7", CombatImpactMetricFormatter.Group(spent, chinese: true));
    }

    [Fact]
    public void Tempo_gained_uses_unsigned_values_in_summary_and_breakdowns()
    {
        var target = new CombatImpactTarget(
            Target,
            4,
            8,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact
        );
        var gained = new CombatImpactGroup(
            CombatImpactKind.AttributeChange,
            "TempoApplyAmount",
            11,
            22,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.AttributeChange,
                "TempoApplyAmount",
                22,
                CombatImpactValueUnit.Amount,
                CombatImpactAuthoritativeBasis.TotalAmount
            ),
            0,
            [target]
        );
        var source = new CombatImpactIncomingSource(
            Target,
            4,
            8,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact
        );
        var incoming = new CombatImpactIncomingGroup(
            CombatImpactKind.AttributeChange,
            "TempoApplyAmount",
            4,
            8,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            [source]
        );

        Assert.Equal("×11 · 22 total", CombatImpactMetricFormatter.Group(gained, chinese: false));
        Assert.Equal("×4 · 8", CombatImpactMetricFormatter.Target(gained, target, chinese: false));
        Assert.Equal("×4 · 8", CombatImpactMetricFormatter.IncomingGroup(incoming, chinese: false));
        Assert.Equal(
            "×4 · 8",
            CombatImpactMetricFormatter.IncomingSource(incoming, source, chinese: false)
        );
    }

    [Fact]
    public void Applied_attribute_actions_keep_counts_in_target_and_source_breakdowns()
    {
        var target = new CombatImpactTarget(
            Target,
            2,
            null,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.None
        );
        var group = new CombatImpactGroup(
            CombatImpactKind.AttributeChange,
            "ForceUseTargets",
            2,
            null,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.None,
            null,
            0,
            [target]
        );
        var incoming = new CombatImpactIncomingGroup(
            CombatImpactKind.AttributeChange,
            "ForceUseTargets",
            2,
            null,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.None,
            [
                new CombatImpactIncomingSource(
                    Target,
                    2,
                    null,
                    CombatImpactValueUnit.Amount,
                    CombatImpactCoverage.None
                ),
            ]
        );

        Assert.Equal("×2", CombatImpactMetricFormatter.Target(group, target, chinese: false));
        Assert.Equal(
            "×2",
            CombatImpactMetricFormatter.IncomingSource(
                incoming,
                Assert.Single(incoming.Sources),
                chinese: false
            )
        );
    }

    [Theory]
    [InlineData((int)CombatImpactOccurrenceBasis.ExplicitExecution, "×3 · 90", "×2 · 60")]
    [InlineData((int)CombatImpactOccurrenceBasis.ReconstructedTransition, "90", "60")]
    public void Attribute_transition_counts_require_explicit_execution_evidence(
        int occurrenceBasisValue,
        string expectedGroup,
        string expectedDetail
    )
    {
        var occurrenceBasis = (CombatImpactOccurrenceBasis)occurrenceBasisValue;
        var target = new CombatImpactTarget(
            Target,
            2,
            60,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.LowerBound
        );
        var group = new CombatImpactGroup(
            CombatImpactKind.AttributeChange,
            "BurnApplyAmount",
            3,
            90,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.LowerBound,
            null,
            0,
            [target]
        )
        {
            Surface = CombatImpactEventSurface.CardAttribute,
            OccurrenceBasis = occurrenceBasis,
        };
        var source = new CombatImpactIncomingSource(
            Target,
            2,
            60,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.LowerBound
        );
        var incoming = new CombatImpactIncomingGroup(
            CombatImpactKind.AttributeChange,
            "BurnApplyAmount",
            3,
            90,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.LowerBound,
            [source]
        )
        {
            Surface = CombatImpactEventSurface.CardAttribute,
            OccurrenceBasis = occurrenceBasis,
        };

        Assert.Equal(expectedGroup, CombatImpactMetricFormatter.Group(group, chinese: false));
        Assert.Equal(
            expectedDetail,
            CombatImpactMetricFormatter.Target(group, target, chinese: false)
        );
        Assert.Equal(
            expectedGroup,
            CombatImpactMetricFormatter.IncomingGroup(incoming, chinese: false)
        );
        Assert.Equal(
            expectedDetail,
            CombatImpactMetricFormatter.IncomingSource(incoming, source, chinese: false)
        );
    }

    [Theory]
    [InlineData((int)CombatImpactCoverage.Exact)]
    [InlineData((int)CombatImpactCoverage.Estimated)]
    [InlineData((int)CombatImpactCoverage.LowerBound)]
    [InlineData((int)CombatImpactCoverage.Partial)]
    public void Coverage_confidence_stays_internal(int coverageValue)
    {
        var group = new CombatImpactGroup(
            CombatImpactKind.AttributeChange,
            "DamageAmount",
            4,
            3514,
            CombatImpactValueUnit.Amount,
            (CombatImpactCoverage)coverageValue,
            null,
            0,
            []
        );

        Assert.Equal("×4 · 3,514", CombatImpactMetricFormatter.Group(group, chinese: false));
        Assert.Equal("×4 · 3,514", CombatImpactMetricFormatter.Group(group, chinese: true));
    }

    [Fact]
    public void Status_removal_amounts_are_unsigned()
    {
        var removed = new CombatImpactGroup(
            CombatImpactKind.AttributeChange,
            "BurnRemoveAmount",
            1,
            6,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            null,
            0,
            []
        );

        Assert.Equal("×1 · 6", CombatImpactMetricFormatter.Group(removed, chinese: false));
        Assert.Equal("×1 · 6", CombatImpactMetricFormatter.Group(removed, chinese: true));
    }

    [Fact]
    public void Target_breakdown_distinguishes_counts_from_authoritative_totals()
    {
        var unquantified = new CombatImpactTarget(
            Target,
            4,
            null,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.None
        );
        var unquantifiedGroup = Group(
            CombatImpactKind.Destroy,
            count: 4,
            observedValue: null,
            CombatImpactValueUnit.Amount
        );
        var observed = new CombatImpactTarget(
            Target,
            10,
            69,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact
        );
        var divergentGroup = new CombatImpactGroup(
            CombatImpactKind.Burn,
            "BurnApplyAmount",
            10,
            69,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Partial,
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.Burn,
                "BurnApplyAmount",
                76,
                CombatImpactValueUnit.Amount,
                CombatImpactAuthoritativeBasis.TotalAmount
            ),
            0,
            [observed]
        );

        Assert.Equal(
            "×4",
            CombatImpactMetricFormatter.Target(unquantifiedGroup, unquantified, chinese: false)
        );
        Assert.Equal(
            "×10 · 76 total",
            CombatImpactMetricFormatter.Group(divergentGroup, chinese: false)
        );
        Assert.Equal(
            "×10 · 69",
            CombatImpactMetricFormatter.Target(divergentGroup, observed, chinese: false)
        );
    }

    [Fact]
    public void Native_count_controls_stay_internal_while_gameplay_totals_remain_visible()
    {
        var applications = new CombatImpactAuthoritativeMetric(
            CombatImpactKind.Haste,
            "HasteAmount",
            10,
            CombatImpactValueUnit.Applications,
            CombatImpactAuthoritativeBasis.ApplicationCount,
            canReconcileApplicationCount: true
        );
        var matching = new CombatImpactGroup(
            CombatImpactKind.Haste,
            "HasteAmount",
            10,
            9500,
            CombatImpactValueUnit.Milliseconds,
            CombatImpactCoverage.LowerBound,
            applications,
            0,
            []
        );
        var divergent = matching with { Count = 7 };
        var estimated = matching with
        {
            ObservedCoverage = CombatImpactCoverage.Estimated,
            AuthoritativeMetric = null,
        };
        var singularApplication = matching with
        {
            Count = 0,
            AuthoritativeMetric = new CombatImpactAuthoritativeMetric(
                CombatImpactKind.Haste,
                "HasteAmount",
                1,
                CombatImpactValueUnit.Applications,
                CombatImpactAuthoritativeBasis.ApplicationCount,
                canReconcileApplicationCount: true
            ),
        };
        var nonComparableCount = matching with
        {
            Count = 23,
            ObservedValue = 69000,
            AuthoritativeMetric = new CombatImpactAuthoritativeMetric(
                CombatImpactKind.Haste,
                "HasteAmount",
                24,
                CombatImpactValueUnit.Applications,
                CombatImpactAuthoritativeBasis.ApplicationCount
            ),
        };
        var authoritativeOnly = new CombatImpactGroup(
            CombatImpactKind.Burn,
            "BurnApplyAmount",
            0,
            null,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.None,
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.Burn,
                "BurnApplyAmount",
                76,
                CombatImpactValueUnit.Amount,
                CombatImpactAuthoritativeBasis.TotalAmount
            ),
            0,
            []
        )
        {
            AmountLedger = new CombatImpactAmountLedger(
                76,
                null,
                CombatImpactCoverage.None,
                0,
                0,
                CombatImpactValueUnit.Amount,
                CombatImpactValueBasis.None,
                false,
                null,
                CombatImpactResidualCoverage.Unknown,
                CombatImpactControlStatus.NotComparable
            ),
        };

        Assert.Equal("×10 · 9.50s", CombatImpactMetricFormatter.Group(matching, chinese: false));
        Assert.Equal("×7 · 9.50s", CombatImpactMetricFormatter.Group(divergent, chinese: false));
        Assert.Equal("×7 · 9.50s", CombatImpactMetricFormatter.Group(divergent, chinese: true));
        Assert.Equal("×10 · 9.50s", CombatImpactMetricFormatter.Group(estimated, chinese: false));
        Assert.Equal("×10 · 9.50s", CombatImpactMetricFormatter.Group(estimated, chinese: true));
        Assert.Equal(
            "9.50s",
            CombatImpactMetricFormatter.Group(singularApplication, chinese: false)
        );
        Assert.Equal(
            "9.50s",
            CombatImpactMetricFormatter.Group(singularApplication, chinese: true)
        );
        Assert.Equal(
            "×23 · 69s",
            CombatImpactMetricFormatter.Group(nonComparableCount, chinese: false)
        );
        Assert.Equal(
            "×23 · 69s",
            CombatImpactMetricFormatter.Group(nonComparableCount, chinese: true)
        );
        Assert.Equal(
            "76 total",
            CombatImpactMetricFormatter.Group(authoritativeOnly, chinese: false)
        );
        Assert.Equal(
            "总计 76",
            CombatImpactMetricFormatter.Group(authoritativeOnly, chinese: true)
        );
    }

    [Fact]
    public void Authoritative_basis_rejects_incompatible_metric_units()
    {
        Assert.Throws<ArgumentException>(() =>
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.Burn,
                "BurnApplyAmount",
                10,
                CombatImpactValueUnit.Applications,
                CombatImpactAuthoritativeBasis.TotalAmount
            )
        );
        Assert.Throws<ArgumentException>(() =>
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.Haste,
                "HasteAmount",
                10,
                CombatImpactValueUnit.Milliseconds,
                CombatImpactAuthoritativeBasis.ApplicationCount
            )
        );
    }

    [Fact]
    public void Incoming_group_uses_the_same_count_and_value_notation_in_both_languages()
    {
        var group = new CombatImpactIncomingGroup(
            CombatImpactKind.Burn,
            "BurnApplyAmount",
            10,
            69,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            []
        );

        Assert.Equal("×10 · 69", CombatImpactMetricFormatter.IncomingGroup(group, chinese: false));
        Assert.Equal("×10 · 69", CombatImpactMetricFormatter.IncomingGroup(group, chinese: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Regen_realized_healing_uses_the_native_healing_icon(bool chinese)
    {
        var group = new CombatImpactGroup(
            CombatImpactKind.AttributeChange,
            "RegenApplyAmount",
            6,
            12,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            null,
            0,
            []
        )
        {
            PeriodicImpact = new CombatImpactPeriodicImpact(
                149,
                0,
                CombatImpactPeriodicProof.Exact,
                PeriodicEffectAttribution.ModelVersion
            ),
        };

        Assert.Equal(
            $"{HealingIcon}149",
            CombatImpactMetricFormatter.PeriodicImpact(group, chinese, healingMarker: HealingIcon)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Periodic_impact_never_falls_back_to_textual_effect_terms(bool chinese)
    {
        var regen = new CombatImpactGroup(
            CombatImpactKind.AttributeChange,
            "RegenApplyAmount",
            6,
            12,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            null,
            0,
            []
        )
        {
            PeriodicImpact = new CombatImpactPeriodicImpact(
                149,
                0,
                CombatImpactPeriodicProof.Exact,
                PeriodicEffectAttribution.ModelVersion
            ),
        };
        var burn = regen with
        {
            Kind = CombatImpactKind.Burn,
            NativeAttributeKey = "BurnApplyAmount",
            PeriodicImpact = new CombatImpactPeriodicImpact(
                73,
                21,
                CombatImpactPeriodicProof.Exact,
                PeriodicEffectAttribution.ModelVersion
            ),
        };

        Assert.Equal("149", CombatImpactMetricFormatter.PeriodicImpact(regen, chinese));
        Assert.Equal("73 · 21", CombatImpactMetricFormatter.PeriodicImpact(burn, chinese));
    }

    private static CombatImpactGroup Group(
        CombatImpactKind kind,
        int count,
        int? observedValue,
        CombatImpactValueUnit unit
    ) =>
        new(
            kind,
            CombatImpactAggregator.NativeKey(kind),
            count,
            observedValue,
            unit,
            observedValue.HasValue ? CombatImpactCoverage.Exact : CombatImpactCoverage.None,
            null,
            0,
            []
        );
}
