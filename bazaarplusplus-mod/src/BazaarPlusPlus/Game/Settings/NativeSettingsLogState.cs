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
        var fields = new BppLogField[] { ("stage", stage), ("reason_code", reasonCode) };
        if (exception == null)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Settings,
                    "settings.native_section.degraded",
                    storm: ["stage", "reason_code"]
                ),
                fields
            );
        else
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Settings,
                    "settings.native_section.degraded",
                    storm: ["stage", "reason_code"]
                ),
                exception,
                fields
            );
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
                new BppLogEvent(
                    BppLogFeatureScope.Settings,
                    "settings.native_section.degraded",
                    storm: ["stage", "reason_code"]
                ),
                ("stage", stage),
                ("reason_code", reasonCode)
            );
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Settings, "settings.native_section.recovered"),
                ("stage", stage)
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
        var fields = new BppLogField[] { ("operation", operation), ("reason_code", reasonCode) };
        if (exception == null)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Settings,
                    "settings.native_section.layout_degraded",
                    storm: ["operation", "reason_code"]
                ),
                fields
            );
        else
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Settings,
                    "settings.native_section.layout_degraded",
                    storm: ["operation", "reason_code"]
                ),
                exception,
                fields
            );
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
                new BppLogEvent(
                    BppLogFeatureScope.Settings,
                    "settings.native_section.layout_degraded",
                    storm: ["operation", "reason_code"]
                ),
                ("operation", operation),
                ("reason_code", reasonCode)
            );
            BppLog.InfoEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Settings,
                    "settings.native_section.layout_recovered"
                ),
                ("operation", operation)
            );
        }

        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.Settings, "settings.native_section.layout_observed"),
            () =>
                [
                    ("operation", operation),
                    ("outcome", outcome),
                    ("affected_count", affectedCount),
                    ("growth_units", growthUnits),
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
