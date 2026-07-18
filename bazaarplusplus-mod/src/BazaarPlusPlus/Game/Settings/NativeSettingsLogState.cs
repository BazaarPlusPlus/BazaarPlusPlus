#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.Settings;

internal static class NativeSettingsLogState
{
    private static readonly OperationalHealthTracker<
        SettingsNativeSectionStage,
        SettingsLogReasonCode
    > InstallHealth = new();
    private static readonly OperationalHealthTracker<
        SettingsNativeLayoutOperation,
        SettingsLogReasonCode
    > LayoutHealth = new();

    internal static void ReportInstallFailure(
        SettingsNativeSectionStage stage,
        SettingsLogReasonCode reasonCode,
        Exception? exception = null
    )
    {
        if (!InstallHealth.ObserveFailure(stage, reasonCode))
            return;
        var fields = new[]
        {
            SettingsLogEvents.NativeSectionDegradedStage.Bind(stage),
            SettingsLogEvents.NativeSectionDegradedReasonCode.Bind(reasonCode),
        };
        if (exception == null)
            BppLog.WarnEvent(SettingsLogEvents.NativeSectionDegraded, fields);
        else
            BppLog.WarnEvent(SettingsLogEvents.NativeSectionDegraded, exception, fields);
    }

    internal static void ReportInstallSuccess()
    {
        foreach (
            SettingsNativeSectionStage stage in Enum.GetValues(typeof(SettingsNativeSectionStage))
        )
        {
            if (!InstallHealth.ObserveSuccess(stage, out var reasonCode))
                continue;
            BppLog.RecoverStorm(
                SettingsLogEvents.NativeSectionDegraded,
                SettingsLogEvents.NativeSectionDegradedStage.Bind(stage),
                SettingsLogEvents.NativeSectionDegradedReasonCode.Bind(reasonCode)
            );
            BppLog.InfoEvent(
                SettingsLogEvents.NativeSectionRecovered,
                SettingsLogEvents.NativeSectionRecoveredStage.Bind(stage)
            );
        }
    }

    internal static void ReportLayoutFailure(
        SettingsNativeLayoutOperation operation,
        SettingsLogReasonCode reasonCode,
        Exception? exception = null
    )
    {
        if (!LayoutHealth.ObserveFailure(operation, reasonCode))
            return;
        var fields = new[]
        {
            SettingsLogEvents.NativeSectionLayoutDegradedOperation.Bind(operation),
            SettingsLogEvents.NativeSectionLayoutDegradedReasonCode.Bind(reasonCode),
        };
        if (exception == null)
            BppLog.WarnEvent(SettingsLogEvents.NativeSectionLayoutDegraded, fields);
        else
            BppLog.WarnEvent(SettingsLogEvents.NativeSectionLayoutDegraded, exception, fields);
    }

    internal static void ReportLayoutSuccess(
        SettingsNativeLayoutOperation operation,
        SettingsNativeLayoutOutcome outcome,
        int affectedCount,
        float growthUnits
    )
    {
        if (LayoutHealth.ObserveSuccess(operation, out var reasonCode))
        {
            BppLog.RecoverStorm(
                SettingsLogEvents.NativeSectionLayoutDegraded,
                SettingsLogEvents.NativeSectionLayoutDegradedOperation.Bind(operation),
                SettingsLogEvents.NativeSectionLayoutDegradedReasonCode.Bind(reasonCode)
            );
            BppLog.InfoEvent(
                SettingsLogEvents.NativeSectionLayoutRecovered,
                SettingsLogEvents.NativeSectionLayoutRecoveredOperation.Bind(operation)
            );
        }

        BppLog.DebugEvent(
            SettingsLogEvents.NativeSectionLayoutObserved,
            () =>
                [
                    SettingsLogEvents.NativeSectionLayoutObservedOperation.Bind(operation),
                    SettingsLogEvents.NativeSectionLayoutObservedOutcome.Bind(outcome),
                    SettingsLogEvents.NativeSectionLayoutObservedAffectedCount.Bind(affectedCount),
                    SettingsLogEvents.NativeSectionLayoutObservedGrowthUnits.Bind(growthUnits),
                ]
        );
    }

    internal static void Reset()
    {
        InstallHealth.Reset();
        LayoutHealth.Reset();
    }
}

internal sealed class NativeSettingsInstallLogAttempt
{
    private readonly List<LayoutObservation> _layoutObservations = [];

    internal void ObserveLayoutFailure(
        SettingsNativeLayoutOperation operation,
        SettingsLogReasonCode reasonCode,
        Exception? exception = null
    ) => _layoutObservations.Add(LayoutObservation.Failed(operation, reasonCode, exception));

    internal void ObserveLayoutSuccess(
        SettingsNativeLayoutOperation operation,
        SettingsNativeLayoutOutcome outcome,
        int affectedCount,
        float growthUnits = 0f
    ) =>
        _layoutObservations.Add(
            LayoutObservation.Succeeded(operation, outcome, affectedCount, growthUnits)
        );

    internal void CommitSuccess()
    {
        NativeSettingsLogState.ReportInstallSuccess();
        foreach (var observation in _layoutObservations)
        {
            if (observation.IsFailure)
            {
                NativeSettingsLogState.ReportLayoutFailure(
                    observation.Operation,
                    observation.ReasonCode,
                    observation.Exception
                );
            }
            else
            {
                NativeSettingsLogState.ReportLayoutSuccess(
                    observation.Operation,
                    observation.Outcome,
                    observation.AffectedCount,
                    observation.GrowthUnits
                );
            }
        }
    }

    private readonly record struct LayoutObservation(
        SettingsNativeLayoutOperation Operation,
        bool IsFailure,
        SettingsLogReasonCode ReasonCode,
        Exception? Exception,
        SettingsNativeLayoutOutcome Outcome,
        int AffectedCount,
        float GrowthUnits
    )
    {
        internal static LayoutObservation Failed(
            SettingsNativeLayoutOperation operation,
            SettingsLogReasonCode reasonCode,
            Exception? exception
        ) => new(operation, true, reasonCode, exception, default, 0, 0f);

        internal static LayoutObservation Succeeded(
            SettingsNativeLayoutOperation operation,
            SettingsNativeLayoutOutcome outcome,
            int affectedCount,
            float growthUnits
        ) => new(operation, false, default, null, outcome, affectedCount, growthUnits);
    }
}
