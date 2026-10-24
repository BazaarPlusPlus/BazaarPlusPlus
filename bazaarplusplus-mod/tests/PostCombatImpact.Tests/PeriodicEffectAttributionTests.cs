using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class PeriodicEffectAttributionTests
{
    private const string DamageIcon = "<sprite name=Damage>";
    private const string ShieldIcon = "<sprite name=Shield>";

    [Fact]
    public void BurnBooksHealthAndShieldWithTwoForOneMitigation()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "burner",
                    CombatActionType.PlayerBurnApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 100, 100),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.Shield, 3, 3),
                        (CombatPlayerAttribute.Burn, 0, 10)
                    )
                ),
                Frame(
                    null,
                    CombatActionType.None,
                    Attributes(
                        (CombatPlayerAttribute.Health, 100, 96),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.Shield, 3, 0),
                        (CombatPlayerAttribute.Burn, 10, 9)
                    ),
                    Adjustment(CombatDamageType.Burn, CombatHealthChange.Shield, -3),
                    Adjustment(CombatDamageType.Burn, CombatHealthChange.Health, -4)
                ),
            },
            ("burner", CombatCardStat.BurnAdded, 10)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("burner"));
        var impact = impacts.SourceImpacts[
            new PeriodicImpactKey("burner", Combatant.Opponent, CombatImpactPeriodicKind.Burn)
        ];

        Assert.Equal(4, impact.HealthAmount);
        Assert.Equal(3, impact.ShieldAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
        Assert.Equal(Combatant.Opponent, Assert.Single(impacts.Allocations).Combatant);
        AssertLedgerReconciles(impacts);

        var report = CombatImpactProjector.Project(simulation, Entities("burner"));
        var burnGroup = Assert.Single(Assert.Single(report.Sources).Groups);
        Assert.Equal(impact, burnGroup.PeriodicImpact);
    }

    [Fact]
    public void Source_impacts_preserve_combatant_before_group_rollup()
    {
        var apply = new CombatFrame
        {
            PlayerUpdates = new CombatPlayerUpdate
            {
                Attributes = Attributes(
                    (CombatPlayerAttribute.Health, 100, 100),
                    (CombatPlayerAttribute.HealthMax, 100, 100),
                    (CombatPlayerAttribute.Burn, 0, 4)
                ),
            },
            OpponentUpdates = new CombatPlayerUpdate
            {
                Attributes = Attributes(
                    (CombatPlayerAttribute.Health, 100, 100),
                    (CombatPlayerAttribute.HealthMax, 100, 100),
                    (CombatPlayerAttribute.Burn, 0, 6)
                ),
            },
        };
        apply.Events.Add(Execution("burner", CombatActionType.PlayerBurnApply, Combatant.Player));
        apply.Events.Add(Execution("burner", CombatActionType.PlayerBurnApply, Combatant.Opponent));
        var tick = new CombatFrame
        {
            PlayerUpdates = new CombatPlayerUpdate
            {
                Attributes = Attributes(
                    (CombatPlayerAttribute.Health, 100, 96),
                    (CombatPlayerAttribute.HealthMax, 100, 100),
                    (CombatPlayerAttribute.Burn, 4, 3)
                ),
                HealthAdjustments =
                {
                    Adjustment(CombatDamageType.Burn, CombatHealthChange.Health, -4),
                },
            },
            OpponentUpdates = new CombatPlayerUpdate
            {
                Attributes = Attributes(
                    (CombatPlayerAttribute.Health, 100, 94),
                    (CombatPlayerAttribute.HealthMax, 100, 100),
                    (CombatPlayerAttribute.Burn, 6, 5)
                ),
                HealthAdjustments =
                {
                    Adjustment(CombatDamageType.Burn, CombatHealthChange.Health, -6),
                },
            },
        };
        var report = PeriodicEffectAttribution.Project(
            Simulation([apply, tick], ("burner", CombatCardStat.BurnAdded, 10)),
            Entities("burner")
        );

        Assert.Equal(
            4,
            report
                .SourceImpacts[
                    new PeriodicImpactKey("burner", Combatant.Player, CombatImpactPeriodicKind.Burn)
                ]
                .HealthAmount
        );
        Assert.Equal(
            6,
            report
                .SourceImpacts[
                    new PeriodicImpactKey(
                        "burner",
                        Combatant.Opponent,
                        CombatImpactPeriodicKind.Burn
                    )
                ]
                .HealthAmount
        );
        AssertLedgerReconciles(report);
    }

    [Fact]
    public void BurnBooksTypedDamageAfterReceivedDamageReduction()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "burner",
                    CombatActionType.PlayerBurnApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 100, 100),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.Shield, 0, 0),
                        (CombatPlayerAttribute.Burn, 0, 10)
                    )
                ),
                Frame(
                    null,
                    CombatActionType.None,
                    Attributes(
                        (CombatPlayerAttribute.Health, 100, 92),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.Shield, 0, 0),
                        (CombatPlayerAttribute.Burn, 10, 9),
                        (CombatPlayerAttribute.PercentDamageReduction, 20, 20)
                    ),
                    Adjustment(CombatDamageType.Burn, CombatHealthChange.Health, -8)
                ),
            },
            ("burner", CombatCardStat.BurnAdded, 10)
        );

        var impact = PeriodicEffectAttribution
            .Project(simulation, Entities("burner"))
            .SourceImpacts[
            new PeriodicImpactKey("burner", Combatant.Opponent, CombatImpactPeriodicKind.Burn)
        ];

        Assert.Equal(8, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
    }

    [Fact]
    public void PoisonAppliesBeforeTheFirstTickAndLaterUsesOpeningOwnership()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "first",
                    CombatActionType.PlayerPoisonApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 1000, 997),
                        (CombatPlayerAttribute.HealthMax, 1000, 1000),
                        (CombatPlayerAttribute.Poison, 0, 3)
                    ),
                    Adjustment(CombatDamageType.Poison, CombatHealthChange.Health, -3)
                ),
                Frame(
                    "second",
                    CombatActionType.PlayerPoisonApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 997, 994),
                        (CombatPlayerAttribute.HealthMax, 1000, 1000),
                        (CombatPlayerAttribute.Poison, 3, 6)
                    ),
                    Adjustment(CombatDamageType.Poison, CombatHealthChange.Health, -3)
                ),
                Frame(
                    null,
                    CombatActionType.None,
                    Attributes(
                        (CombatPlayerAttribute.Health, 994, 988),
                        (CombatPlayerAttribute.HealthMax, 1000, 1000),
                        (CombatPlayerAttribute.Poison, 6, 6)
                    ),
                    Adjustment(CombatDamageType.Poison, CombatHealthChange.Health, -6)
                ),
            },
            ("first", CombatCardStat.PoisonAdded, 3),
            ("second", CombatCardStat.PoisonAdded, 3)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("first", "second"));
        var first = impacts.SourceImpacts[
            new PeriodicImpactKey("first", Combatant.Opponent, CombatImpactPeriodicKind.Poison)
        ];
        var second = impacts.SourceImpacts[
            new PeriodicImpactKey("second", Combatant.Opponent, CombatImpactPeriodicKind.Poison)
        ];

        Assert.Equal(9, first.HealthAmount);
        Assert.Equal(3, second.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Proportional, first.Proof);
        Assert.Equal(CombatImpactPeriodicProof.Proportional, second.Proof);
    }

    [Fact]
    public void PoisonBooksTypedDamageAfterReceivedDamageReduction()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "poisoner",
                    CombatActionType.PlayerPoisonApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 100, 100),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.Poison, 0, 10)
                    )
                ),
                Frame(
                    null,
                    CombatActionType.None,
                    Attributes(
                        (CombatPlayerAttribute.Health, 100, 92),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.Poison, 10, 10),
                        (CombatPlayerAttribute.PercentDamageReduction, 20, 20)
                    ),
                    Adjustment(CombatDamageType.Poison, CombatHealthChange.Health, -8)
                ),
            },
            ("poisoner", CombatCardStat.PoisonAdded, 10)
        );

        var impact = PeriodicEffectAttribution
            .Project(simulation, Entities("poisoner"))
            .SourceImpacts[
            new PeriodicImpactKey("poisoner", Combatant.Opponent, CombatImpactPeriodicKind.Poison)
        ];

        Assert.Equal(8, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
    }

    [Fact]
    public void RegenBooksOnlyHealingAcceptedByTheHealthCap()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "regenerator",
                    CombatActionType.PlayerRegenApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 95, 95),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.HealthRegen, 0, 10)
                    )
                ),
                Frame(
                    null,
                    CombatActionType.None,
                    Attributes(
                        (CombatPlayerAttribute.Health, 95, 100),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.HealthRegen, 10, 10)
                    ),
                    Adjustment(CombatDamageType.Regen, CombatHealthChange.Health, 10)
                ),
            },
            ("regenerator", CombatCardStat.RegenAdded, 10)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("regenerator"));
        var impact = impacts.SourceImpacts[
            new PeriodicImpactKey("regenerator", Combatant.Opponent, CombatImpactPeriodicKind.Regen)
        ];

        Assert.Equal(5, impact.HealthAmount);
        Assert.Equal(0, impact.ShieldAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
    }

    [Fact]
    public void RegenBooksTypedHealingAfterSameFrameStatusDecay()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "regenerator",
                    CombatActionType.PlayerRegenApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 50, 50),
                        (CombatPlayerAttribute.HealthMax, 200, 200),
                        (CombatPlayerAttribute.HealthRegen, 0, 100)
                    )
                ),
                Frame(
                    null,
                    CombatActionType.None,
                    Attributes(
                        (CombatPlayerAttribute.Health, 50, 147),
                        (CombatPlayerAttribute.HealthMax, 200, 200),
                        (CombatPlayerAttribute.HealthRegen, 100, 97)
                    ),
                    Adjustment(CombatDamageType.Regen, CombatHealthChange.Health, 97)
                ),
            },
            ("regenerator", CombatCardStat.RegenAdded, 100)
        );

        var impact = PeriodicEffectAttribution
            .Project(simulation, Entities("regenerator"))
            .SourceImpacts[
            new PeriodicImpactKey("regenerator", Combatant.Opponent, CombatImpactPeriodicKind.Regen)
        ];

        Assert.Equal(97, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
    }

    [Fact]
    public void HealthLedgerLetsDamageCreateRoomForSameFrameRegen()
    {
        var setup = Frame(
            "poisoner",
            CombatActionType.PlayerPoisonApply,
            Attributes(
                (CombatPlayerAttribute.Health, 100, 100),
                (CombatPlayerAttribute.HealthMax, 100, 100),
                (CombatPlayerAttribute.Poison, 0, 30),
                (CombatPlayerAttribute.HealthRegen, 0, 50)
            )
        );
        setup.Events.Add(Execution("regenerator", CombatActionType.PlayerRegenApply));
        var tick = Frame(
            null,
            CombatActionType.None,
            Attributes(
                (CombatPlayerAttribute.Health, 100, 100),
                (CombatPlayerAttribute.HealthMax, 100, 100),
                (CombatPlayerAttribute.Poison, 30, 30),
                (CombatPlayerAttribute.HealthRegen, 50, 50)
            ),
            Adjustment(CombatDamageType.Poison, CombatHealthChange.Health, -30),
            Adjustment(CombatDamageType.Regen, CombatHealthChange.Health, 50)
        );
        var simulation = Simulation(
            new[] { setup, tick },
            ("poisoner", CombatCardStat.PoisonAdded, 30),
            ("regenerator", CombatCardStat.RegenAdded, 50)
        );

        var impacts = PeriodicEffectAttribution.Project(
            simulation,
            Entities("poisoner", "regenerator")
        );

        Assert.Equal(
            30,
            impacts
                .SourceImpacts[
                    new PeriodicImpactKey(
                        "poisoner",
                        Combatant.Opponent,
                        CombatImpactPeriodicKind.Poison
                    )
                ]
                .HealthAmount
        );
        Assert.Equal(
            30,
            impacts
                .SourceImpacts[
                    new PeriodicImpactKey(
                        "regenerator",
                        Combatant.Opponent,
                        CombatImpactPeriodicKind.Regen
                    )
                ]
                .HealthAmount
        );
    }

    [Fact]
    public void PeriodicDamageAfterAnEarlierLethalAdjustmentIsNotRealized()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "poisoner",
                    CombatActionType.PlayerPoisonApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 10, 10),
                        (CombatPlayerAttribute.HealthMax, 10, 10),
                        (CombatPlayerAttribute.Poison, 0, 7)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (CombatPlayerAttribute.Health, 10, -7),
                        (CombatPlayerAttribute.HealthMax, 10, 10)
                    ),
                    Adjustment(CombatDamageType.Damage, CombatHealthChange.Health, -10),
                    Adjustment(CombatDamageType.Poison, CombatHealthChange.Health, -7)
                ),
            },
            ("poisoner", CombatCardStat.PoisonAdded, 7)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("poisoner"));

        Assert.Empty(impacts.SourceImpacts);
    }

    [Fact]
    public void DeathEventRemainsTerminalAfterLaterNumericHealing()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "poisoner",
                    CombatActionType.PlayerPoisonApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 10, 10),
                        (CombatPlayerAttribute.HealthMax, 10, 10),
                        (CombatPlayerAttribute.Poison, 0, 5)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (CombatPlayerAttribute.Health, 10, 5),
                        (CombatPlayerAttribute.HealthMax, 10, 10)
                    ),
                    Adjustment(CombatDamageType.Damage, CombatHealthChange.Health, -20),
                    Adjustment(CombatDamageType.Heal, CombatHealthChange.Health, 20),
                    Adjustment(CombatDamageType.Poison, CombatHealthChange.Health, -5)
                ),
            },
            ("poisoner", CombatCardStat.PoisonAdded, 5)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("poisoner"));

        Assert.Empty(impacts.SourceImpacts);
    }

    [Fact]
    public void PeriodicDamageBeforeALaterLethalAdjustmentStillCounts()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "burner",
                    CombatActionType.PlayerBurnApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 15, 15),
                        (CombatPlayerAttribute.HealthMax, 15, 15),
                        (CombatPlayerAttribute.Burn, 0, 5)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (CombatPlayerAttribute.Health, 15, -10),
                        (CombatPlayerAttribute.HealthMax, 15, 15)
                    ),
                    Adjustment(CombatDamageType.Burn, CombatHealthChange.Health, -5),
                    Adjustment(CombatDamageType.Damage, CombatHealthChange.Health, -20)
                ),
            },
            ("burner", CombatCardStat.BurnAdded, 5)
        );

        var impact = PeriodicEffectAttribution
            .Project(simulation, Entities("burner"))
            .SourceImpacts[
            new PeriodicImpactKey("burner", Combatant.Opponent, CombatImpactPeriodicKind.Burn)
        ];

        Assert.Equal(5, impact.HealthAmount);
    }

    [Fact]
    public void BurnShieldConsumptionAfterLethalHealthDamageIsNotRealized()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "burner",
                    CombatActionType.PlayerBurnApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 10, 10),
                        (CombatPlayerAttribute.HealthMax, 10, 10),
                        (CombatPlayerAttribute.Shield, 5, 5),
                        (CombatPlayerAttribute.Burn, 0, 20)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (CombatPlayerAttribute.Health, 10, 0),
                        (CombatPlayerAttribute.HealthMax, 10, 10),
                        (CombatPlayerAttribute.Shield, 5, 0)
                    ),
                    Adjustment(CombatDamageType.Damage, CombatHealthChange.Health, -10),
                    Adjustment(CombatDamageType.Burn, CombatHealthChange.Shield, -5)
                ),
            },
            ("burner", CombatCardStat.BurnAdded, 20)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("burner"));

        Assert.Empty(impacts.SourceImpacts);
    }

    [Fact]
    public void BurnShieldConsumptionBeforeLethalHealthDamageStillCounts()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "burner",
                    CombatActionType.PlayerBurnApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 10, 10),
                        (CombatPlayerAttribute.HealthMax, 10, 10),
                        (CombatPlayerAttribute.Shield, 5, 5),
                        (CombatPlayerAttribute.Burn, 0, 20)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (CombatPlayerAttribute.Health, 10, 0),
                        (CombatPlayerAttribute.HealthMax, 10, 10),
                        (CombatPlayerAttribute.Shield, 5, 0)
                    ),
                    Adjustment(CombatDamageType.Burn, CombatHealthChange.Shield, -5),
                    Adjustment(CombatDamageType.Damage, CombatHealthChange.Health, -10)
                ),
            },
            ("burner", CombatCardStat.BurnAdded, 20)
        );

        var impact = PeriodicEffectAttribution
            .Project(simulation, Entities("burner"))
            .SourceImpacts[
            new PeriodicImpactKey("burner", Combatant.Opponent, CombatImpactPeriodicKind.Burn)
        ];

        Assert.Equal(5, impact.ShieldAmount);
    }

    [Fact]
    public void BurnHealthAndShieldAdjustmentsShareOneAtomicTick()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "burner",
                    CombatActionType.PlayerBurnApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 5, 5),
                        (CombatPlayerAttribute.HealthMax, 5, 5),
                        (CombatPlayerAttribute.Shield, 5, 5),
                        (CombatPlayerAttribute.Burn, 0, 20)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (CombatPlayerAttribute.Health, 5, -15),
                        (CombatPlayerAttribute.HealthMax, 5, 5),
                        (CombatPlayerAttribute.Shield, 5, 0)
                    ),
                    Adjustment(CombatDamageType.Burn, CombatHealthChange.Health, -20),
                    Adjustment(CombatDamageType.Burn, CombatHealthChange.Shield, -5)
                ),
            },
            ("burner", CombatCardStat.BurnAdded, 20)
        );

        var impact = PeriodicEffectAttribution
            .Project(simulation, Entities("burner"))
            .SourceImpacts[
            new PeriodicImpactKey("burner", Combatant.Opponent, CombatImpactPeriodicKind.Burn)
        ];

        Assert.Equal(5, impact.HealthAmount);
        Assert.Equal(5, impact.ShieldAmount);
    }

    [Fact]
    public void RegenBeforeLethalDamageStillCountsOnDeathFrame()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "regenerator",
                    CombatActionType.PlayerRegenApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 90, 90),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.HealthRegen, 0, 10)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (CombatPlayerAttribute.Health, 90, -50),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.HealthRegen, 10, 10)
                    ),
                    Adjustment(CombatDamageType.Regen, CombatHealthChange.Health, 10),
                    Adjustment(CombatDamageType.Damage, CombatHealthChange.Health, -150)
                ),
            },
            ("regenerator", CombatCardStat.RegenAdded, 10)
        );

        var impact = PeriodicEffectAttribution
            .Project(simulation, Entities("regenerator"))
            .SourceImpacts[
            new PeriodicImpactKey("regenerator", Combatant.Opponent, CombatImpactPeriodicKind.Regen)
        ];

        Assert.Equal(10, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
    }

    [Fact]
    public void RegenCanRescueFromNonPositiveHealthWithoutADeathEvent()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "regenerator",
                    CombatActionType.PlayerRegenApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 10, 10),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.HealthRegen, 0, 20)
                    )
                ),
                Frame(
                    null,
                    CombatActionType.None,
                    Attributes(
                        (CombatPlayerAttribute.Health, 10, 10),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.HealthRegen, 20, 20)
                    ),
                    Adjustment(CombatDamageType.Damage, CombatHealthChange.Health, -20),
                    Adjustment(CombatDamageType.Regen, CombatHealthChange.Health, 20)
                ),
            },
            ("regenerator", CombatCardStat.RegenAdded, 20)
        );

        var impact = PeriodicEffectAttribution
            .Project(simulation, Entities("regenerator"))
            .SourceImpacts[
            new PeriodicImpactKey("regenerator", Combatant.Opponent, CombatImpactPeriodicKind.Regen)
        ];

        Assert.Equal(20, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
    }

    [Fact]
    public void StatusAbsentRegenDoesNotUseUnobservedStaticAttributeCandidate()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    null,
                    CombatActionType.None,
                    Attributes(
                        (CombatPlayerAttribute.Health, 90, 100),
                        (CombatPlayerAttribute.HealthMax, 100, 100)
                    ),
                    Adjustment(CombatDamageType.Regen, CombatHealthChange.Health, 10)
                ),
            }
        );
        var entities = EntitiesOwnedBy(Combatant.Opponent, "regenerator")
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        entities["regenerator"] = entities["regenerator"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.RegenApplyAmount] = 10,
            },
        };

        var impacts = PeriodicEffectAttribution.Project(simulation, entities);

        Assert.Empty(impacts.SourceImpacts);
        var gap = Assert.Single(impacts.Residuals);
        Assert.Equal(10, gap.HealthAmount);
        Assert.Equal(PeriodicUnknownOrigin.StatusStateAbsent, gap.Origin);
    }

    [Fact]
    public void InitialRegenUsesWholeCombatCandidateOnlyAsApproximate()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    null,
                    CombatActionType.None,
                    Attributes(
                        (CombatPlayerAttribute.Health, 90, 100),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.HealthRegen, 10, 10)
                    ),
                    Adjustment(CombatDamageType.Regen, CombatHealthChange.Health, 10)
                ),
            },
            ("possible-source", CombatCardStat.RegenAdded, 10)
        );

        var impacts = PeriodicEffectAttribution.Project(
            simulation,
            EntitiesOwnedBy(Combatant.Opponent, "possible-source")
        );
        var impact = impacts.SourceImpacts[
            new PeriodicImpactKey(
                "possible-source",
                Combatant.Opponent,
                CombatImpactPeriodicKind.Regen
            )
        ];

        Assert.Equal(10, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Proportional, impact.Proof);
    }

    [Fact]
    public void InitialRegenFallsBackToLaterObservedRegenSourcesAsApproximate()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    null,
                    CombatActionType.None,
                    Attributes(
                        (CombatPlayerAttribute.Health, 50, 60),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.HealthRegen, 10, 10)
                    ),
                    Adjustment(CombatDamageType.Regen, CombatHealthChange.Health, 10)
                ),
                Frame(
                    "regenerator",
                    CombatActionType.PlayerRegenApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 60, 40),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.HealthRegen, 10, 20)
                    ),
                    Adjustment(CombatDamageType.Damage, CombatHealthChange.Health, -20)
                ),
                Frame(
                    null,
                    CombatActionType.None,
                    Attributes(
                        (CombatPlayerAttribute.Health, 40, 60),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.HealthRegen, 20, 20)
                    ),
                    Adjustment(CombatDamageType.Regen, CombatHealthChange.Health, 20)
                ),
            },
            ("regenerator", CombatCardStat.RegenAdded, 10)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("regenerator"));
        var impact = impacts.SourceImpacts[
            new PeriodicImpactKey("regenerator", Combatant.Opponent, CombatImpactPeriodicKind.Regen)
        ];

        Assert.Equal(30, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Proportional, impact.Proof);
    }

    [Fact]
    public void StatusResetClosesTheOldOwnershipEpoch()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "old-source",
                    CombatActionType.PlayerBurnApply,
                    Attributes((CombatPlayerAttribute.Burn, 0, 10))
                ),
                Frame(null, CombatActionType.None, Attributes((CombatPlayerAttribute.Burn, 10, 0))),
                Frame(
                    "new-source",
                    CombatActionType.PlayerBurnApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 100, 94),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.Shield, 0, 0),
                        (CombatPlayerAttribute.Burn, 0, 6)
                    ),
                    Adjustment(CombatDamageType.Burn, CombatHealthChange.Health, -6)
                ),
            },
            ("old-source", CombatCardStat.BurnAdded, 10),
            ("new-source", CombatCardStat.BurnAdded, 6)
        );

        var impacts = PeriodicEffectAttribution.Project(
            simulation,
            Entities("old-source", "new-source")
        );

        Assert.DoesNotContain(
            new PeriodicImpactKey("old-source", Combatant.Opponent, CombatImpactPeriodicKind.Burn),
            impacts.SourceImpacts
        );
        var impact = impacts.SourceImpacts[
            new PeriodicImpactKey("new-source", Combatant.Opponent, CombatImpactPeriodicKind.Burn)
        ];
        Assert.Equal(6, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
    }

    [Fact]
    public void UnresolvedApplySourceRemainsUnknown()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "missing-source",
                    CombatActionType.PlayerPoisonApply,
                    Attributes(
                        (CombatPlayerAttribute.Health, 100, 90),
                        (CombatPlayerAttribute.HealthMax, 100, 100),
                        (CombatPlayerAttribute.Poison, 0, 10)
                    ),
                    Adjustment(CombatDamageType.Poison, CombatHealthChange.Health, -10)
                ),
            }
        );
        var impacts = PeriodicEffectAttribution.Project(simulation, Entities());

        Assert.Empty(impacts.SourceImpacts);
        var gap = Assert.Single(impacts.Residuals);
        Assert.Equal(10, gap.HealthAmount);
        Assert.Equal(PeriodicUnknownOrigin.MissingApplySource, gap.Origin);
        AssertLedgerReconciles(impacts);
    }

    [Fact]
    public void FormatterKeepsProportionalProofInternalAndUsesNativeImpactIcons()
    {
        var group = new CombatImpactGroup(
            CombatImpactKind.Burn,
            CombatImpactAggregator.NativeKey(CombatImpactKind.Burn),
            1,
            10,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.Burn,
                CombatImpactAggregator.NativeKey(CombatImpactKind.Burn),
                10,
                CombatImpactValueUnit.Amount,
                CombatImpactAuthoritativeBasis.TotalAmount
            ),
            0,
            []
        )
        {
            PeriodicImpact = new CombatImpactPeriodicImpact(
                7,
                2,
                CombatImpactPeriodicProof.Proportional,
                PeriodicEffectAttribution.ModelVersion
            ),
        };

        Assert.Equal("×1 · 10 total", CombatImpactMetricFormatter.Group(group, chinese: false));
        Assert.Equal("×1 · 总计 10", CombatImpactMetricFormatter.Group(group, chinese: true));
        Assert.Equal(
            $"{DamageIcon}7 · {ShieldIcon}2",
            CombatImpactMetricFormatter.PeriodicImpact(
                group,
                chinese: false,
                damageMarker: DamageIcon,
                shieldMarker: ShieldIcon
            )
        );
        Assert.Equal(
            $"{DamageIcon}7 · {ShieldIcon}2",
            CombatImpactMetricFormatter.PeriodicImpact(
                group,
                chinese: true,
                damageMarker: DamageIcon,
                shieldMarker: ShieldIcon
            )
        );
    }

    private static CombatTimeline Simulation(
        IReadOnlyList<CombatFrame> frames,
        params (string SourceId, CombatCardStat Stat, int Value)[] stats
    )
    {
        var simulation = new CombatTimeline { Frames = frames.ToList() };
        foreach (var (sourceId, stat, value) in stats)
        {
            if (!simulation.CardStats.TryGetValue(sourceId, out var sourceStats))
            {
                sourceStats = new Dictionary<CombatCardStat, int>();
                simulation.CardStats[sourceId] = sourceStats;
            }
            sourceStats[stat] = value;
        }
        return simulation;
    }

    private static CombatFrame Frame(
        string? sourceId,
        CombatActionType action,
        IReadOnlyDictionary<CombatPlayerAttribute, CombatPlayerAttributeUpdate> attributes,
        params CombatHealthAdjustment[] adjustments
    )
    {
        var frame = new CombatFrame
        {
            OpponentUpdates = new CombatPlayerUpdate
            {
                Attributes = new Dictionary<CombatPlayerAttribute, CombatPlayerAttributeUpdate>(
                    attributes
                ),
                HealthAdjustments = adjustments.ToList(),
            },
        };
        if (sourceId != null)
            frame.Events.Add(Execution(sourceId, action));
        return frame;
    }

    private static CombatFrame DeathFrame(
        IReadOnlyDictionary<CombatPlayerAttribute, CombatPlayerAttributeUpdate> attributes,
        params CombatHealthAdjustment[] adjustments
    )
    {
        var frame = Frame(null, CombatActionType.None, attributes, adjustments);
        frame.Events.Add(new CombatantDied { CombatantId = Combatant.Opponent });
        return frame;
    }

    private static CombatEffectExecuted Execution(
        string sourceId,
        CombatActionType action,
        Combatant target = Combatant.Opponent
    ) =>
        new()
        {
            ActionType = action,
            Source = (sourceId),
            Target = new CombatPlayerTarget { Target = target },
        };

    private static Dictionary<CombatPlayerAttribute, CombatPlayerAttributeUpdate> Attributes(
        params (CombatPlayerAttribute Type, int Previous, int Current)[] values
    ) =>
        values.ToDictionary(
            value => value.Type,
            value => new CombatPlayerAttributeUpdate
            {
                AttributeType = value.Type,
                PreviousValue = value.Previous,
                CurrentValue = value.Current,
            }
        );

    private static CombatHealthAdjustment Adjustment(
        CombatDamageType damageType,
        CombatHealthChange pool,
        int amount
    ) =>
        new()
        {
            DamageType = damageType,
            AttributeChanged = pool,
            Amount = amount,
        };

    private static IReadOnlyDictionary<string, CombatImpactEntity> Entities(
        params string[] sourceIds
    ) => EntitiesOwnedBy(Combatant.Player, sourceIds);

    private static IReadOnlyDictionary<string, CombatImpactEntity> EntitiesOwnedBy(
        Combatant sourceCombatant,
        params string[] sourceIds
    )
    {
        var entities = sourceIds.ToDictionary(
            sourceId => sourceId,
            sourceId => new CombatImpactEntity(
                sourceId,
                sourceId,
                "Item",
                null,
                0,
                CombatantId: sourceCombatant
            ),
            StringComparer.Ordinal
        );
        var opponentId = CombatImpactProjector.PlayerId(Combatant.Opponent);
        entities[opponentId] = new CombatImpactEntity(
            opponentId,
            "Opponent",
            "Hero",
            null,
            entities.Count,
            CombatantId: Combatant.Opponent
        );
        return entities;
    }

    private static void AssertLedgerReconciles(PeriodicAttributionReport report)
    {
        var booked = report
            .Allocations.Select(allocation => new
            {
                Key = new PeriodicMeasurementKey(allocation.Combatant, allocation.Kind),
                allocation.HealthAmount,
                allocation.ShieldAmount,
            })
            .Concat(
                report.Residuals.Select(residual => new
                {
                    Key = new PeriodicMeasurementKey(residual.Combatant, residual.Kind),
                    residual.HealthAmount,
                    residual.ShieldAmount,
                })
            )
            .GroupBy(entry => entry.Key)
            .ToDictionary(
                group => group.Key,
                group => new PeriodicMeasuredAmounts(
                    group.Sum(entry => entry.HealthAmount),
                    group.Sum(entry => entry.ShieldAmount)
                )
            );

        foreach (var key in report.MeasuredTotals.Keys.Union(booked.Keys))
        {
            report.MeasuredTotals.TryGetValue(key, out var measured);
            booked.TryGetValue(key, out var accounted);
            Assert.Equal(measured, accounted);
        }

        var sourceRollups = report
            .Allocations.GroupBy(allocation => new PeriodicImpactKey(
                allocation.SourceId,
                allocation.Combatant,
                allocation.Kind
            ))
            .ToArray();
        Assert.Equal(sourceRollups.Length, report.SourceImpacts.Count);
        foreach (var rollup in sourceRollups)
        {
            var impact = report.SourceImpacts[rollup.Key];
            Assert.Equal(rollup.Sum(allocation => allocation.HealthAmount), impact.HealthAmount);
            Assert.Equal(rollup.Sum(allocation => allocation.ShieldAmount), impact.ShieldAmount);
            Assert.Equal(
                rollup.Any(allocation => allocation.Proof == CombatImpactPeriodicProof.Proportional)
                    ? CombatImpactPeriodicProof.Proportional
                    : CombatImpactPeriodicProof.Exact,
                impact.Proof
            );
        }
    }
}
