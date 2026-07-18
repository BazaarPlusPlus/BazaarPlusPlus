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

        if (probe is not ITypedEncounterStateProbe typed)
        {
            ReportSuccess();
            return probe.GetChoicePedestal();
        }

        var outcome = typed.GetChoicePedestalOutcome();
        if (outcome.IsSuccess)
        {
            ReportSuccess();
            return outcome.Snapshot;
        }

        if (Health.ObserveFailure(TooltipEncounterProbe.Encounter, outcome.FailureReason))
        {
            var fields = new[]
            {
                TooltipLogEvents.EncounterProbeDegradedProbe.Bind(TooltipEncounterProbe.Encounter),
                TooltipLogEvents.EncounterProbeDegradedReasonCode.Bind(outcome.FailureReason),
            };
            if (outcome.Exception == null)
                BppLog.WarnEvent(TooltipLogEvents.EncounterProbeDegraded, fields);
            else
                BppLog.WarnEvent(
                    TooltipLogEvents.EncounterProbeDegraded,
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
            TooltipLogEvents.EncounterProbeDegraded,
            TooltipLogEvents.EncounterProbeDegradedProbe.Bind(TooltipEncounterProbe.Encounter),
            TooltipLogEvents.EncounterProbeDegradedReasonCode.Bind(reasonCode)
        );
        BppLog.InfoEvent(
            TooltipLogEvents.EncounterProbeRecovered,
            TooltipLogEvents.EncounterProbeRecoveredProbe.Bind(TooltipEncounterProbe.Encounter)
        );
    }
}
