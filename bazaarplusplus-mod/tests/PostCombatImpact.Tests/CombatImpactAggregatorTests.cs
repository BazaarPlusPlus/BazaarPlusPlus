using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactAggregatorTests
{
    [Fact]
    public void Groups_effects_by_source_kind_and_target()
    {
        var report = Aggregate([
            Event(CombatImpactKind.Slow, "fairies", "eclipse", 2900, milliseconds: true),
            Event(CombatImpactKind.Slow, "fairies", "eclipse", 2950, milliseconds: true),
            Event(CombatImpactKind.Slow, "fairies", "bread", 1950, milliseconds: true),
            Event(CombatImpactKind.Freeze, "fairies", "eclipse", 3850, milliseconds: true),
        ]);

        var source = Assert.Single(report.Sources);
        Assert.Equal("Fairies", source.Entity.Name);
        Assert.Equal(4, source.TotalCount);
        Assert.Collection(
            source.Groups,
            slow =>
            {
                Assert.Equal(CombatImpactKind.Slow, slow.Kind);
                Assert.Equal(3, slow.Count);
                Assert.Equal(7800, slow.ObservedValue);
                Assert.Collection(
                    slow.Targets,
                    eclipse =>
                    {
                        Assert.Equal("The Eclipse", eclipse.Entity.Name);
                        Assert.Equal(2, eclipse.Count);
                        Assert.Equal(5850, eclipse.ObservedValue);
                    },
                    bread =>
                    {
                        Assert.Equal("Bread Knife", bread.Entity.Name);
                        Assert.Equal(1, bread.Count);
                        Assert.Equal(1950, bread.ObservedValue);
                    }
                );
            },
            freeze =>
            {
                Assert.Equal(CombatImpactKind.Freeze, freeze.Kind);
                Assert.Equal(1, freeze.Count);
            }
        );
    }

    [Fact]
    public void Keeps_authoritative_total_separate_from_exact_observed_target_values()
    {
        var report = Aggregate(
            [Event(CombatImpactKind.DirectDamage, "fairies", "opponent", 80)],
            authoritative:
            [
                new CombatImpactAuthoritativeMetric(
                    CombatImpactKind.DirectDamage,
                    CombatImpactAggregator.NativeKey(CombatImpactKind.DirectDamage),
                    240,
                    CombatImpactValueUnit.Amount,
                    CombatImpactAuthoritativeBasis.TotalAmount
                ),
            ]
        );

        var group = Assert.Single(Assert.Single(report.Sources).Groups);
        Assert.Equal(80, group.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Exact, group.ObservedCoverage);
        Assert.Equal(240, group.AuthoritativeMetric?.Value);
        Assert.Equal(CombatImpactAuthoritativeBasis.TotalAmount, group.AuthoritativeMetric?.Basis);
        var target = Assert.Single(group.Targets);
        Assert.Equal(80, target.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Exact, target.ObservedCoverage);
        Assert.Equal(160, group.AmountLedger.ResidualAmount);
        Assert.Equal(CombatImpactResidualCoverage.Exact, group.AmountLedger.ResidualCoverage);
    }

    [Fact]
    public void Aggregator_never_merges_typed_attribute_metrics_with_different_native_keys()
    {
        var report = Aggregate([
            Event(
                CombatImpactKind.AttributeChange,
                "fairies",
                "bread",
                -20,
                nativeKey: "DamageAmount"
            ),
            Event(CombatImpactKind.AttributeChange, "fairies", "bread", 2, nativeKey: "CritChance"),
        ]);

        var groups = Assert.Single(report.Sources).Groups;
        Assert.Equal(2, groups.Count);
        var damage = Assert.Single(groups, group => group.NativeAttributeKey == "DamageAmount");
        Assert.Equal(-20, damage.ObservedValue);
        Assert.Equal(-20, Assert.Single(damage.Targets).ObservedValue);
        Assert.Contains(groups, group => group.NativeAttributeKey == "CritChance");
    }

    [Fact]
    public void Applied_regen_total_stays_separate_from_card_regen_gain_targets()
    {
        var report = Aggregate(
            [
                Event(
                    CombatImpactKind.AttributeChange,
                    "fairies",
                    "opponent",
                    338,
                    nativeKey: "RegenApplyAmount"
                ),
                Event(
                    CombatImpactKind.AttributeChange,
                    "fairies",
                    "bread",
                    16,
                    nativeKey: "RegenApplyAmount",
                    surface: CombatImpactEventSurface.CardAttribute
                ),
            ],
            authoritative:
            [
                new CombatImpactAuthoritativeMetric(
                    CombatImpactKind.AttributeChange,
                    "RegenApplyAmount",
                    338,
                    CombatImpactValueUnit.Amount,
                    CombatImpactAuthoritativeBasis.TotalAmount
                ),
            ]
        );

        var groups = Assert.Single(report.Sources).Groups;
        Assert.Equal(2, groups.Count);
        var applied = Assert.Single(
            groups,
            group => group.Surface == CombatImpactEventSurface.AppliedEffect
        );
        Assert.Equal(338, applied.AuthoritativeMetric?.Value);
        var cardGain = Assert.Single(
            groups,
            group => group.Surface == CombatImpactEventSurface.CardAttribute
        );
        Assert.Null(cardGain.AuthoritativeMetric);
        Assert.Equal(16, cardGain.ObservedValue);
        Assert.Equal("bread", Assert.Single(cardGain.Targets).Entity.Id);
    }

    [Fact]
    public void Applied_regen_total_hides_internal_breakdown_state_when_only_card_regen_gain_exists()
    {
        var report = Aggregate(
            [
                Event(
                    CombatImpactKind.AttributeChange,
                    "fairies",
                    "bread",
                    16,
                    nativeKey: "RegenApplyAmount",
                    surface: CombatImpactEventSurface.CardAttribute
                ),
            ],
            authoritative:
            [
                new CombatImpactAuthoritativeMetric(
                    CombatImpactKind.AttributeChange,
                    "RegenApplyAmount",
                    338,
                    CombatImpactValueUnit.Amount,
                    CombatImpactAuthoritativeBasis.TotalAmount
                ),
            ]
        );

        var groups = Assert.Single(report.Sources).Groups;
        Assert.Equal(2, groups.Count);
        var applied = Assert.Single(
            groups,
            group => group.Surface == CombatImpactEventSurface.AppliedEffect
        );
        Assert.Equal(0, applied.Count);
        Assert.Null(applied.ObservedValue);
        Assert.Equal(338, applied.AuthoritativeMetric?.Value);
        Assert.Equal("338 total", CombatImpactMetricFormatter.Group(applied, chinese: false));
        Assert.Equal("总计 338", CombatImpactMetricFormatter.Group(applied, chinese: true));

        var cardGain = Assert.Single(
            groups,
            group => group.Surface == CombatImpactEventSurface.CardAttribute
        );
        Assert.Null(cardGain.AuthoritativeMetric);
        Assert.Equal(16, cardGain.ObservedValue);
        Assert.Equal("bread", Assert.Single(cardGain.Targets).Entity.Id);
    }

    [Theory]
    [InlineData(true, (int)CombatImpactOccurrenceBasis.ExplicitExecution)]
    [InlineData(false, (int)CombatImpactOccurrenceBasis.ReconstructedTransition)]
    public void Occurrence_basis_is_explicit_only_when_every_aggregated_event_is_explicit(
        bool secondEventIsExplicit,
        int expectedValue
    )
    {
        var expected = (CombatImpactOccurrenceBasis)expectedValue;
        var report = Aggregate([
            Event(
                CombatImpactKind.AttributeChange,
                "fairies",
                "bread",
                40,
                nativeKey: "BurnApplyAmount",
                surface: CombatImpactEventSurface.CardAttribute,
                occurrenceBasis: CombatImpactOccurrenceBasis.ExplicitExecution
            ),
            Event(
                CombatImpactKind.AttributeChange,
                "fairies",
                "bread",
                50,
                nativeKey: "BurnApplyAmount",
                surface: CombatImpactEventSurface.CardAttribute,
                occurrenceBasis: secondEventIsExplicit
                    ? CombatImpactOccurrenceBasis.ExplicitExecution
                    : CombatImpactOccurrenceBasis.ReconstructedTransition
            ),
        ]);

        Assert.Equal(expected, Assert.Single(Assert.Single(report.Sources).Groups).OccurrenceBasis);
        Assert.Equal(
            expected,
            Assert.Single(Assert.Single(report.Received).Groups).OccurrenceBasis
        );
    }

    [Fact]
    public void Critical_metadata_remains_nested_in_its_effect_group()
    {
        var report = Aggregate([
            Event(CombatImpactKind.AttributeChange, "fairies", "bread", 160, isCritical: true),
        ]);

        var source = Assert.Single(report.Sources);
        var group = Assert.Single(source.Groups);
        Assert.Equal(1, source.TotalCount);
        Assert.Equal(1, group.CriticalCount);
        Assert.Equal(1, group.CriticalOutcomeCount);
        Assert.Equal(160, group.CriticalObservedValue);
        var target = Assert.Single(group.Targets);
        Assert.Equal("Bread Knife", target.Entity.Name);
        Assert.Equal(160, target.ObservedValue);

        var received = Assert.Single(report.Received);
        Assert.Equal(1, received.TotalCount);
        var incoming = Assert.Single(received.Groups);
        Assert.Equal(1, incoming.Count);
        Assert.Equal(1, incoming.CriticalCount);
        Assert.Equal(1, incoming.CriticalOutcomeCount);
        Assert.Equal(160, incoming.CriticalObservedValue);
        var incomingSource = Assert.Single(incoming.Sources);
        Assert.Equal("Fairies", incomingSource.Entity.Name);
        Assert.Equal(1, incomingSource.Count);
        Assert.Equal(160, incomingSource.ObservedValue);
    }

    [Fact]
    public void Entity_rows_omit_unknown_sources_and_stay_stable_across_insertion_order()
    {
        var forwardEntities = new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal)
        {
            ["beta"] = Entity("beta", "Same", "Item", 0),
            ["target-b"] = Entity("target-b", "Same", "Item", 0),
            ["alpha"] = Entity("alpha", "Same", "Item", 0),
            ["target-a"] = Entity("target-a", "Same", "Item", 0),
        };
        var reverseEntities = forwardEntities
            .Reverse()
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var forwardEvents = new[]
        {
            Event(CombatImpactKind.Burn, "unknown", "target-a", 10),
            Event(CombatImpactKind.Haste, "beta", "target-b", 1000, milliseconds: true),
            Event(CombatImpactKind.Haste, "alpha", "target-b", 1000, milliseconds: true),
            Event(CombatImpactKind.Haste, "beta", "target-a", 1000, milliseconds: true),
            Event(CombatImpactKind.Haste, "alpha", "target-a", 1000, milliseconds: true),
        };

        var forward = Aggregate(forwardEntities, forwardEvents);
        var reverse = Aggregate(reverseEntities, forwardEvents.Reverse().ToArray());

        Assert.Equal(["alpha", "beta"], forward.Sources.Select(source => source.Entity.Id));
        Assert.Equal(
            ["target-a", "target-b"],
            Assert
                .Single(forward.Sources, source => source.Entity.Id == "alpha")
                .Groups.Single()
                .Targets.Select(target => target.Entity.Id)
        );
        Assert.Equal(
            ["target-a", "target-b"],
            forward.Received.Select(received => received.Entity.Id)
        );
        Assert.Equal(SnapshotOrder(forward), SnapshotOrder(reverse));
    }

    [Fact]
    public void Preserves_authoritative_only_group_without_inventing_events_or_targets()
    {
        var report = Aggregate(
            [],
            authoritative:
            [
                new CombatImpactAuthoritativeMetric(
                    CombatImpactKind.Burn,
                    CombatImpactAggregator.NativeKey(CombatImpactKind.Burn),
                    76,
                    CombatImpactValueUnit.Amount,
                    CombatImpactAuthoritativeBasis.TotalAmount
                ),
            ]
        );

        var source = Assert.Single(report.Sources);
        var group = Assert.Single(source.Groups);

        Assert.Equal(0, source.EffectCount);
        Assert.Equal(0, group.Count);
        Assert.Null(group.ObservedValue);
        Assert.Equal(76, group.AuthoritativeMetric?.Value);
        Assert.Empty(group.Targets);
        Assert.Empty(report.Received);
    }

    [Fact]
    public void Projects_received_effects_by_exact_target_with_attributed_sources()
    {
        var report = Aggregate([
            Event(CombatImpactKind.Slow, "fairies", "bread", 2900, milliseconds: true),
            Event(CombatImpactKind.Slow, "fairies", "bread", milliseconds: true),
            Event(CombatImpactKind.Burn, "eclipse", "bread", 8),
        ]);

        var received = Assert.Single(report.Received);
        Assert.Equal("Bread Knife", received.Entity.Name);
        Assert.Equal(3, received.EffectCount);
        Assert.Collection(
            received.Groups,
            burn =>
            {
                Assert.Equal(CombatImpactKind.Burn, burn.Kind);
                Assert.Equal(1, burn.Count);
                var source = Assert.Single(burn.Sources);
                Assert.Equal("The Eclipse", source.Entity.Name);
                Assert.Equal(8, source.ObservedValue);
            },
            slow =>
            {
                Assert.Equal(CombatImpactKind.Slow, slow.Kind);
                Assert.Equal(2, slow.Count);
                Assert.Equal(2900, slow.ObservedValue);
                Assert.Equal(CombatImpactCoverage.Partial, slow.ObservedCoverage);
                var source = Assert.Single(slow.Sources);
                Assert.Equal("Fairies", source.Entity.Name);
                Assert.Equal(2, source.Count);
                Assert.Equal(2900, source.ObservedValue);
                Assert.Equal(CombatImpactCoverage.Partial, source.ObservedCoverage);
            }
        );
    }

    [Fact]
    public void Discloses_unresolved_target_occurrences_instead_of_silently_dropping_them()
    {
        var report = Aggregate([Event(CombatImpactKind.Burn, "fairies", "missing-target", 5)]);

        var group = Assert.Single(Assert.Single(report.Sources).Groups);
        Assert.Equal(1, group.Count);
        Assert.Equal(1, group.UnresolvedTargetCount);
        Assert.Empty(group.Targets);
        Assert.Empty(report.Received);
    }

    [Fact]
    public void Coverage_distinguishes_mixed_units_from_net_frame_lower_bounds()
    {
        var report = Aggregate([
            Event(CombatImpactKind.Haste, "fairies", "bread", 5),
            Event(CombatImpactKind.Haste, "fairies", "bread", 1000, milliseconds: true),
        ]);

        var group = Assert.Single(Assert.Single(report.Sources).Groups);
        var target = Assert.Single(group.Targets);

        Assert.Equal(2, group.Count);
        Assert.Null(group.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Partial, group.ObservedCoverage);
        Assert.Null(target.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Partial, target.ObservedCoverage);

        var reconstructed = Aggregate([
            Event(
                CombatImpactKind.Haste,
                "fairies",
                "bread",
                950,
                milliseconds: true,
                valueBasis: CombatImpactValueBasis.NetFrameDelta
            ),
        ]);

        var reconstructedGroup = Assert.Single(Assert.Single(reconstructed.Sources).Groups);
        var reconstructedTarget = Assert.Single(reconstructedGroup.Targets);

        Assert.Equal(950, reconstructedGroup.ObservedValue);
        Assert.Equal(CombatImpactCoverage.LowerBound, reconstructedGroup.ObservedCoverage);
        Assert.Equal(CombatImpactCoverage.LowerBound, reconstructedTarget.ObservedCoverage);
    }

    [Fact]
    public void Attribute_groups_expose_when_their_values_move_in_both_directions()
    {
        var report = Aggregate([
            Event(
                CombatImpactKind.AttributeChange,
                "fairies",
                "bread",
                18,
                nativeKey: "ShieldApplyAmount",
                surface: CombatImpactEventSurface.CardAttribute
            ),
            Event(
                CombatImpactKind.AttributeChange,
                "fairies",
                "bread",
                -100,
                nativeKey: "ShieldApplyAmount",
                surface: CombatImpactEventSurface.CardAttribute
            ),
        ]);

        var caused = Assert.Single(Assert.Single(report.Sources).Groups);
        var received = Assert.Single(Assert.Single(report.Received).Groups);

        Assert.Equal(-82, caused.ObservedValue);
        Assert.True(caused.HasMixedValueDirections);
        Assert.Equal(-82, received.ObservedValue);
        Assert.True(received.HasMixedValueDirections);
    }

    [Fact]
    public void Trigger_ledger_conserves_applications_without_reusing_activation_batches()
    {
        var events = new[]
        {
            Event(CombatImpactKind.Charge, "fairies", "bread") with
            {
                RawDirectSourceId = "fairies",
                TriggerSourceId = "bread",
                TriggerFrameIndex = 0,
                TriggerScope = CombatImpactTriggerScope.AttributedExternal,
            },
            Event(CombatImpactKind.Charge, "fairies", "eclipse") with
            {
                RawDirectSourceId = "fairies",
                TriggerSourceId = "bread",
                TriggerFrameIndex = 0,
                TriggerScope = CombatImpactTriggerScope.AttributedExternal,
            },
            Event(CombatImpactKind.Charge, "fairies", "bread") with
            {
                RawDirectSourceId = "fairies",
                TriggerSourceId = "fairies",
                TriggerFrameIndex = 1,
                TriggerScope = CombatImpactTriggerScope.AttributedSelf,
            },
            Event(CombatImpactKind.Charge, "fairies", "bread") with
            {
                TriggerScope = CombatImpactTriggerScope.NoTriggerEvidence,
            },
        };

        var source = Assert.Single(Aggregate(events).Sources);
        var group = Assert.Single(source.Groups);
        var classified =
            group.TriggerSources.Sum(item => item.ApplicationCount)
            + group.UnattributedTriggerApplicationCount
            + group.TriggerFallbackApplicationCount
            + group.NoTriggerEvidenceApplicationCount
            + group.NotApplicableTriggerApplicationCount;

        Assert.Equal(group.Count, classified);
        Assert.Equal(2, source.ObservedActivationBatchCount);
        Assert.Equal(3, group.TriggerSources.Sum(item => item.ApplicationCount));
        Assert.Equal(2, group.TriggerSources.Sum(item => item.ObservedActivationBatchCount));
        Assert.Equal(1, group.NoTriggerEvidenceApplicationCount);
        Assert.Equal(
            CombatImpactTriggerPresentationState.PartialBreakdown,
            group.TriggerPresentationState
        );
    }

    [Fact]
    public void Trigger_presentation_states_distinguish_complete_hidden_failed_and_unscoped_groups()
    {
        CombatImpactGroup GroupFor(CombatImpactEvent item) =>
            Assert.Single(Assert.Single(Aggregate([item]).Sources).Groups);

        var self = GroupFor(
            Event(CombatImpactKind.Charge, "fairies", "bread") with
            {
                TriggerSourceId = "fairies",
                TriggerFrameIndex = 0,
                TriggerScope = CombatImpactTriggerScope.AttributedSelf,
            }
        );
        var external = GroupFor(
            Event(CombatImpactKind.Charge, "fairies", "bread") with
            {
                TriggerSourceId = "bread",
                TriggerFrameIndex = 0,
                TriggerScope = CombatImpactTriggerScope.AttributedExternal,
            }
        );
        var unresolved = GroupFor(
            Event(CombatImpactKind.Charge, "fairies", "bread") with
            {
                TriggerScope = CombatImpactTriggerScope.Unattributed,
            }
        );
        var fallback = GroupFor(
            Event(CombatImpactKind.Charge, "fairies", "bread") with
            {
                TriggerSourceId = "bread",
                TriggerFrameIndex = 0,
                TriggerScope = CombatImpactTriggerScope.AttributedViaTriggerFallback,
            }
        );
        var noEvidence = GroupFor(
            Event(CombatImpactKind.Charge, "fairies", "bread") with
            {
                TriggerScope = CombatImpactTriggerScope.NoTriggerEvidence,
            }
        );
        var notApplicable = GroupFor(Event(CombatImpactKind.Charge, "fairies", "bread"));

        Assert.Equal(
            CombatImpactTriggerPresentationState.HiddenSelfOnly,
            self.TriggerPresentationState
        );
        Assert.Equal(
            CombatImpactTriggerPresentationState.Complete,
            external.TriggerPresentationState
        );
        Assert.Equal(
            CombatImpactTriggerPresentationState.BreakdownUnavailable,
            unresolved.TriggerPresentationState
        );
        Assert.Equal(
            CombatImpactTriggerPresentationState.BreakdownUnavailable,
            fallback.TriggerPresentationState
        );
        Assert.Equal(
            CombatImpactTriggerPresentationState.None,
            noEvidence.TriggerPresentationState
        );
        Assert.Equal(
            CombatImpactTriggerPresentationState.None,
            notApplicable.TriggerPresentationState
        );
        Assert.Equal(1, noEvidence.NoTriggerEvidenceApplicationCount);
        Assert.Equal(1, notApplicable.NotApplicableTriggerApplicationCount);
    }

    [Fact]
    public void Incoming_groups_balance_missing_source_entities_explicitly()
    {
        var entities = new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal)
        {
            ["bread"] = Entity("bread", "Bread Knife", "Item", 0),
        };

        var report = Aggregate(
            entities,
            [Event(CombatImpactKind.Burn, "missing-source", "bread", 5)]
        );
        var received = Assert.Single(report.Received);
        var group = Assert.Single(received.Groups);

        Assert.Empty(report.Sources);
        Assert.Empty(group.Sources);
        Assert.Equal(1, group.UnresolvedSourceCount);
        Assert.Equal(
            group.Count,
            group.Sources.Sum(item => item.Count) + group.UnresolvedSourceCount
        );
    }

    [Fact]
    public void Control_ledgers_distinguish_exact_upper_bound_and_application_residuals()
    {
        var exact = Aggregate(
            [
                Event(CombatImpactKind.Burn, "fairies", "bread", 30),
                Event(CombatImpactKind.Burn, "fairies", "eclipse"),
            ],
            [
                new CombatImpactAuthoritativeMetric(
                    CombatImpactKind.Burn,
                    "BurnApplyAmount",
                    50,
                    CombatImpactValueUnit.Amount,
                    CombatImpactAuthoritativeBasis.TotalAmount
                ),
            ]
        );
        var exactLedger = Assert.Single(Assert.Single(exact.Sources).Groups).AmountLedger;
        Assert.Equal(20, exactLedger.ResidualAmount);
        Assert.Equal(CombatImpactResidualCoverage.Exact, exactLedger.ResidualCoverage);

        var lowerBound = Aggregate(
            [
                Event(
                    CombatImpactKind.Burn,
                    "fairies",
                    "bread",
                    30,
                    valueBasis: CombatImpactValueBasis.NetFrameDelta
                ),
            ],
            [
                new CombatImpactAuthoritativeMetric(
                    CombatImpactKind.Burn,
                    "BurnApplyAmount",
                    50,
                    CombatImpactValueUnit.Amount,
                    CombatImpactAuthoritativeBasis.TotalAmount
                ),
            ]
        );
        var lowerBoundLedger = Assert.Single(Assert.Single(lowerBound.Sources).Groups).AmountLedger;
        Assert.Equal(20, lowerBoundLedger.ResidualAmount);
        Assert.Equal(CombatImpactResidualCoverage.UpperBound, lowerBoundLedger.ResidualCoverage);

        var estimated = Aggregate(
            [
                Event(
                    CombatImpactKind.Haste,
                    "fairies",
                    "bread",
                    30,
                    valueBasis: CombatImpactValueBasis.ConfiguredActionAmount
                ),
            ],
            [
                new CombatImpactAuthoritativeMetric(
                    CombatImpactKind.Haste,
                    "HasteAmount",
                    50,
                    CombatImpactValueUnit.Amount,
                    CombatImpactAuthoritativeBasis.TotalAmount
                ),
            ]
        );
        var estimatedLedger = Assert.Single(Assert.Single(estimated.Sources).Groups).AmountLedger;
        Assert.Equal(CombatImpactCoverage.Estimated, estimatedLedger.ObservedCoverage);
        Assert.False(estimatedLedger.ComparableToAuthoritativeTotal);
        Assert.Null(estimatedLedger.ResidualAmount);
        Assert.Equal(CombatImpactResidualCoverage.Unknown, estimatedLedger.ResidualCoverage);
        Assert.Equal(CombatImpactControlStatus.NotComparable, estimatedLedger.ControlStatus);

        var applications = Aggregate(
            [
                Event(CombatImpactKind.Haste, "fairies", "bread", nativeKey: "HasteAmount"),
                Event(CombatImpactKind.Haste, "fairies", "eclipse", nativeKey: "HasteAmount"),
            ],
            [
                new CombatImpactAuthoritativeMetric(
                    CombatImpactKind.Haste,
                    "HasteAmount",
                    3,
                    CombatImpactValueUnit.Applications,
                    CombatImpactAuthoritativeBasis.ApplicationCount,
                    canReconcileApplicationCount: true
                ),
            ]
        );
        var applicationLedger = Assert
            .Single(Assert.Single(applications.Sources).Groups)
            .ApplicationLedger;
        Assert.Equal(1, applicationLedger.ApplicationResidual);
        Assert.Equal(CombatImpactControlStatus.PositiveResidual, applicationLedger.ControlStatus);

        var affectedCards = Aggregate(
            [Event(CombatImpactKind.Haste, "fairies", "bread", nativeKey: "HasteAmount")],
            [
                new CombatImpactAuthoritativeMetric(
                    CombatImpactKind.Haste,
                    "HasteAmount",
                    2,
                    CombatImpactValueUnit.Applications,
                    CombatImpactAuthoritativeBasis.ApplicationCount
                ),
            ]
        );
        var affectedCardsLedger = Assert
            .Single(Assert.Single(affectedCards.Sources).Groups)
            .ApplicationLedger;
        Assert.False(affectedCardsLedger.ComparableToAuthoritativeCount);
        Assert.Null(affectedCardsLedger.ApplicationResidual);
        Assert.Equal(CombatImpactControlStatus.NotComparable, affectedCardsLedger.ControlStatus);
    }

    [Fact]
    public void Control_ledgers_reject_negative_residuals_as_over_observed()
    {
        var amount = Aggregate(
            [Event(CombatImpactKind.Burn, "fairies", "bread", 60)],
            [
                new CombatImpactAuthoritativeMetric(
                    CombatImpactKind.Burn,
                    "BurnApplyAmount",
                    50,
                    CombatImpactValueUnit.Amount,
                    CombatImpactAuthoritativeBasis.TotalAmount
                ),
            ]
        );
        var amountLedger = Assert.Single(Assert.Single(amount.Sources).Groups).AmountLedger;
        Assert.Equal(CombatImpactControlStatus.OverObserved, amountLedger.ControlStatus);
        Assert.Null(amountLedger.ResidualAmount);

        var applications = Aggregate(
            [
                Event(CombatImpactKind.Haste, "fairies", "bread", nativeKey: "HasteAmount"),
                Event(CombatImpactKind.Haste, "fairies", "eclipse", nativeKey: "HasteAmount"),
            ],
            [
                new CombatImpactAuthoritativeMetric(
                    CombatImpactKind.Haste,
                    "HasteAmount",
                    1,
                    CombatImpactValueUnit.Applications,
                    CombatImpactAuthoritativeBasis.ApplicationCount,
                    canReconcileApplicationCount: true
                ),
            ]
        );
        var applicationLedger = Assert
            .Single(Assert.Single(applications.Sources).Groups)
            .ApplicationLedger;
        Assert.Equal(CombatImpactControlStatus.OverObserved, applicationLedger.ControlStatus);
        Assert.Null(applicationLedger.ApplicationResidual);
    }

    private static CombatImpactReport Aggregate(
        IReadOnlyList<CombatImpactEvent> events,
        IReadOnlyList<CombatImpactAuthoritativeMetric>? authoritative = null
    )
    {
        var entities = new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal)
        {
            ["fairies"] = Entity("fairies", "Fairies", "Skill", 0),
            ["bread"] = Entity("bread", "Bread Knife", "Item", 1),
            ["eclipse"] = Entity("eclipse", "The Eclipse", "Item", 2),
            ["opponent"] = Entity("opponent", "Opponent", "Hero", 3),
        };
        var metrics =
            authoritative == null
                ? new Dictionary<string, IReadOnlyList<CombatImpactAuthoritativeMetric>>(
                    StringComparer.Ordinal
                )
                : new Dictionary<string, IReadOnlyList<CombatImpactAuthoritativeMetric>>(
                    StringComparer.Ordinal
                )
                {
                    ["fairies"] = authoritative,
                };
        return CombatImpactAggregator.Aggregate(
            new CombatImpactProjectionInput(
                entities,
                events,
                new Dictionary<string, int>(),
                metrics
            )
        );
    }

    private static CombatImpactReport Aggregate(
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyList<CombatImpactEvent> events
    ) =>
        CombatImpactAggregator.Aggregate(
            new CombatImpactProjectionInput(
                entities,
                events,
                new Dictionary<string, int>(),
                new Dictionary<string, IReadOnlyList<CombatImpactAuthoritativeMetric>>()
            )
        );

    private static string SnapshotOrder(CombatImpactReport report) =>
        string.Join(
            "|",
            report
                .Sources.Select(source =>
                    $"S:{source.Entity.Id}:{string.Join(",", source.Groups.SelectMany(group => group.Targets).Select(target => target.Entity.Id))}"
                )
                .Concat(
                    report.Received.Select(received =>
                        $"R:{received.Entity.Id}:{string.Join(",", received.Groups.SelectMany(group => group.Sources).Select(source => source.Entity.Id))}"
                    )
                )
        );

    private static CombatImpactEvent Event(
        CombatImpactKind kind,
        string source,
        string target,
        int? value = null,
        bool milliseconds = false,
        string? nativeKey = null,
        bool isCritical = false,
        CombatImpactValueBasis valueBasis = CombatImpactValueBasis.ExactAdjustment,
        CombatImpactEventSurface surface = CombatImpactEventSurface.AppliedEffect,
        CombatImpactOccurrenceBasis occurrenceBasis =
            CombatImpactOccurrenceBasis.ReconstructedTransition
    ) =>
        new(
            kind,
            source,
            target,
            value,
            milliseconds ? CombatImpactValueUnit.Milliseconds : CombatImpactValueUnit.Amount,
            nativeKey,
            isCritical,
            valueBasis
        )
        {
            Surface = surface,
            OccurrenceBasis = occurrenceBasis,
        };

    private static CombatImpactEntity Entity(string id, string name, string type, int order) =>
        new(id, name, type, null, order);
}
