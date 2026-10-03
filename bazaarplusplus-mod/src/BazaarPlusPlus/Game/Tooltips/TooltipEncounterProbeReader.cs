#nullable enable
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class TooltipEncounterProbeReader
{
    private static readonly OperationalHealthTracker<
        TooltipEncounterProbe,
        EncounterProbeFailureReason
    > Health = new();

    internal static ChoicePedestalSnapshot? ReadChoice(IEncounterStateProbe? probe)
    {
        if (probe == null)
            return null;

        var outcome = probe.GetChoicePedestalOutcome();
        if (outcome.IsSuccess)
        {
            ReportSuccess();
            return outcome.Snapshot;
        }

        if (Health.ObserveFailure(TooltipEncounterProbe.Encounter, outcome.FailureReason))
        {
            var fields = new BppLogField[]
            {
                ("probe", TooltipEncounterProbe.Encounter),
                ("reason_code", outcome.FailureReason),
            };
            if (outcome.Exception == null)
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Tooltips,
                        "tooltips.encounter_probe.degraded",
                        storm: ["probe", "reason_code"]
                    ),
                    fields
                );
            else
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Tooltips,
                        "tooltips.encounter_probe.degraded",
                        storm: ["probe", "reason_code"]
                    ),
                    outcome.Exception,
                    fields
                );
        }
        return outcome.Snapshot;
    }

    internal static void Reset() => Health.Reset();

    private static void ReportSuccess()
    {
        if (!Health.ObserveSuccess(TooltipEncounterProbe.Encounter, out var reasonCode))
            return;
        BppLog.RecoverStorm(
            new BppLogEvent(
                BppLogFeatureScope.Tooltips,
                "tooltips.encounter_probe.degraded",
                storm: ["probe", "reason_code"]
            ),
            ("probe", TooltipEncounterProbe.Encounter),
            ("reason_code", reasonCode)
        );
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Tooltips, "tooltips.encounter_probe.recovered"),
            ("probe", TooltipEncounterProbe.Encounter)
        );
    }
}
