#nullable enable
using System.Text;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.TestSupport;

/// <summary>The acceptance rules a projected corpus must hold, and its evidence golden.</summary>
internal static class CorpusAcceptance
{
    internal static void Validate(CorpusInventoryReport report)
    {
        if (report.InvalidPayloads.Count > 0)
            throw new InvalidOperationException(
                $"Corpus contained invalid payloads: {string.Join(",", report.InvalidPayloads)}"
            );
        var cardAttributes = report.CardAttributeAttribution;
        if (cardAttributes.DiagnosticGroups == 0 || cardAttributes.DiagnosedClaimants == 0)
        {
            throw new InvalidOperationException(
                "Card-attribute attribution diagnostics were vacuous."
            );
        }
        if (
            cardAttributes.ConcurrentExactGroups
                + cardAttributes.SingleUnknownSolvedGroups
                + cardAttributes.ResidualGroups
            == 0
        )
        {
            throw new InvalidOperationException(
                "Card-attribute attribution diagnostics observed no concurrent claimant group."
            );
        }
        if (cardAttributes.ConservationFailures != 0)
        {
            throw new InvalidOperationException(
                $"Card-attribute attribution failed conservation in {cardAttributes.ConservationFailures} groups."
            );
        }
        if (
            cardAttributes.ResidualObservations.Any(observation =>
                observation.Resolution
                    == CombatImpactAttributeTransitionResolution.ConcurrentResidual.ToString()
                && observation.FailureReasons.Count == 0
            )
        )
        {
            throw new InvalidOperationException(
                "A residual card-attribute group was emitted without an auditable failure reason."
            );
        }
        if (
            report.Sources.ResolvedActions == 0
            || report.Sources.MissingStatSourceIdentities.Count > 0
        )
        {
            throw new InvalidOperationException(
                "Periodic source inventory was vacuous or missed an authoritative CardStats identity."
            );
        }
        if (report.Scenarios.PoisonShieldAdjustment != 0)
            throw new InvalidOperationException(
                "Poison unexpectedly consumed Shield in the corpus."
            );

        foreach (
            var (name, candidate) in new[]
            {
                ("Burn Shield bridge", report.Rules.BurnShieldBridge),
                ("Burn decay", report.Rules.BurnDecay),
                ("Poison tick", report.Rules.PoisonTick),
                ("Regen tick", report.Rules.RegenTick),
            }
        )
        {
            if (candidate.Observed == 0)
            {
                throw new InvalidOperationException(
                    $"{name} diagnostic was vacuous: observed={candidate.Observed}."
                );
            }
        }

        if (report.Rules.ShieldLedger.Mismatched != 0)
            throw new InvalidOperationException("Shield pool ledger did not reconcile.");
        if (
            report.TerminalImpact.CombatantsWithMultipleDeathEvents != 0
            || report.TerminalImpact.FramesAfterFirstDeathEvent != 0
            || report.TerminalImpact.PeriodicAdjustmentFramesAfterFirstDeathEvent != 0
        )
        {
            throw new InvalidOperationException(
                "Terminal periodic-impact policy encountered a replay that continued after death."
            );
        }
        if (report.Attribution.Results == 0)
            throw new InvalidOperationException("Attribution model produced no source results.");
        if (
            report.Attribution.Results
            != report.Attribution.ExactResults + report.Attribution.ProportionalResults
        )
        {
            throw new InvalidOperationException(
                "Attribution result counts did not reconcile across proof classes."
            );
        }
        if (
            report.Attribution.HealthByKind.Values.Sum()
                != report.Attribution.ExactHealthAmount
                    + report.Attribution.ProportionalHealthAmount
            || report.Attribution.ShieldByKind.Values.Sum()
                != report.Attribution.ExactShieldAmount
                    + report.Attribution.ProportionalShieldAmount
        )
        {
            throw new InvalidOperationException(
                "Attributed amounts did not reconcile across proof classes."
            );
        }
        if (
            report.Attribution.AllocationDecisions
            != report.Attribution.ExactAllocationDecisions
                + report.Attribution.ProportionalAllocationDecisions
        )
        {
            throw new InvalidOperationException(
                "Allocation decision counts did not reconcile across proof classes."
            );
        }
        if (
            report.Attribution.AllocatedHealthByKindAndProof.Values.Sum()
                != report.Attribution.HealthByKind.Values.Sum()
            || report.Attribution.AllocatedShieldByKindAndProof.Values.Sum()
                != report.Attribution.ShieldByKind.Values.Sum()
        )
        {
            throw new InvalidOperationException(
                "Allocation decision amounts did not reconcile with attributed totals."
            );
        }
        if (
            report.Attribution.RegenPoolAcceptedOnDeathEventFrames
            != report.Attribution.RegenRealizedOnDeathEventFrames
                + report.Attribution.RegenPoolAcceptedAfterNonPositiveHealthOnDeathEventFrames
        )
        {
            throw new InvalidOperationException(
                "Death-frame Regen did not partition into pre-lethal realized and post-lethal excluded amounts."
            );
        }
        var totals = report.Rules.RealizedTotals;
        RequireTerminalMeasurement(
            "Burn Health",
            report.Adjustments.GetValueOrDefault("Burn/Health/Loss")?.AbsoluteAmount ?? 0,
            report.TerminalImpact.PeriodicHealthLossOnDeathEventFrames.GetValueOrDefault("Burn"),
            report.TerminalImpact.AtomicBurnGroupCombatEffectiveHealthLossOnDeathEventFrames,
            totals.BurnHealthDamage
        );
        RequireTerminalMeasurement(
            "Burn Shield",
            report.Adjustments.GetValueOrDefault("Burn/Shield/Loss")?.AbsoluteAmount ?? 0,
            report.TerminalImpact.BurnShieldConsumedOnDeathEventFrames,
            report.TerminalImpact.AtomicBurnGroupCombatEffectiveShieldConsumedOnDeathEventFrames,
            totals.BurnShieldConsumed
        );
        RequireTerminalMeasurement(
            "Poison Health",
            report.Adjustments.GetValueOrDefault("Poison/Health/Loss")?.AbsoluteAmount ?? 0,
            report.TerminalImpact.PeriodicHealthLossOnDeathEventFrames.GetValueOrDefault("Poison"),
            report.TerminalImpact.CombatEffectivePeriodicHealthLossOnDeathEventFrames.GetValueOrDefault(
                "Poison"
            ),
            totals.PoisonHealthDamage
        );
        RequireConservation(
            "Burn Health",
            report.Attribution.HealthByKind.GetValueOrDefault("Burn"),
            report.Attribution.UnattributedHealthByKind.GetValueOrDefault("Burn"),
            totals.BurnHealthDamage
        );
        RequireConservation(
            "Burn Shield",
            report.Attribution.ShieldByKind.GetValueOrDefault("Burn"),
            report.Attribution.UnattributedShieldByKind.GetValueOrDefault("Burn"),
            totals.BurnShieldConsumed
        );
        RequireConservation(
            "Poison Health",
            report.Attribution.HealthByKind.GetValueOrDefault("Poison"),
            report.Attribution.UnattributedHealthByKind.GetValueOrDefault("Poison"),
            totals.PoisonHealthDamage
        );
        RequireConservation(
            "Regen Health",
            report.Attribution.HealthByKind.GetValueOrDefault("Regen"),
            report.Attribution.UnattributedHealthByKind.GetValueOrDefault("Regen"),
            totals.RegenRealized
        );
        if (
            report.Attribution.ShieldByKind.GetValueOrDefault("Poison") != 0
            || report.Attribution.ShieldByKind.GetValueOrDefault("Regen") != 0
        )
        {
            throw new InvalidOperationException(
                "Attribution model booked non-Burn periodic Shield impact."
            );
        }
        var attributedRegen = report.Attribution.HealthByKind.GetValueOrDefault("Regen");
        if (report.Attribution.RegenStableSpawnBaselineAttributed != 0)
        {
            throw new InvalidOperationException(
                "Stable spawn Regen baseline was attributed to an item or skill: "
                    + report.Attribution.RegenStableSpawnBaselineAttributed
            );
        }
        var sourceAttributableRegen =
            totals.RegenRealized - report.Attribution.RegenStableSpawnBaselineRealized;
        if (attributedRegen * 100 < sourceAttributableRegen * 94)
        {
            throw new InvalidOperationException(
                "Regen source-attributable realized-impact coverage regressed below 94%: "
                    + $"{attributedRegen}/{sourceAttributableRegen}."
            );
        }
    }

    private static void RequireTerminalMeasurement(
        string name,
        long raw,
        long rawOnDeathFrames,
        long effectiveOnDeathFrames,
        long measured
    )
    {
        var expected = raw - rawOnDeathFrames + effectiveOnDeathFrames;
        if (measured != expected)
        {
            throw new InvalidOperationException(
                $"{name} terminal measurement did not reconcile: "
                    + $"raw={raw} death_raw={rawOnDeathFrames} "
                    + $"death_effective={effectiveOnDeathFrames} measured={measured}."
            );
        }
    }

    private static void RequireConservation(
        string name,
        long attributed,
        long unknown,
        long measured
    )
    {
        if (attributed < 0 || unknown < 0 || attributed + unknown != measured)
        {
            throw new InvalidOperationException(
                $"{name} attribution did not conserve its measured ledger: "
                    + $"attributed={attributed} unknown={unknown} measured={measured}."
            );
        }
    }

    /// <summary>
    /// Compares the evidence with its committed golden byte for byte, or rewrites the golden
    /// under <c>BPP_UPDATE_GOLDENS=1</c>.
    /// </summary>
    internal static void CompareWithGolden(string goldenPath, string actual)
    {
        if (Environment.GetEnvironmentVariable("BPP_UPDATE_GOLDENS") == "1")
        {
            File.WriteAllText(
                Path.Combine(TestInputs.RepoRoot, goldenPath),
                actual,
                new UTF8Encoding(false)
            );
            Console.WriteLine($"Rewrote {goldenPath}; review it with git diff.");
            return;
        }
        var expected = TestInputs.Fixture(goldenPath).Replace("\r\n", "\n");
        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return;
        throw new InvalidOperationException(
            $"Combat Impact evidence differs from {goldenPath}:\n"
                + UnifiedDiff.Render(expected, actual, goldenPath)
                + "\nIf the change is intended, regenerate with BPP_UPDATE_GOLDENS=1 "
                + "just mod::test-core (or dotnet run --project "
                + "tests/CombatImpact.Corpus/CombatImpact.Corpus.csproj), then review git diff."
        );
    }
}
