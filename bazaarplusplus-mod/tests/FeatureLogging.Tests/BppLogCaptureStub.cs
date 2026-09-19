#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Infrastructure;

internal sealed record CapturedBppLogEvent(
    string Severity,
    BppLogEventDefinition Definition,
    BppLogFieldValue[] Values,
    Exception? Exception
);

internal sealed record CapturedStormRecovery(
    BppLogEventDefinition Definition,
    BppLogFieldValue[] Values
);

internal static class BppLog
{
    private static readonly List<CapturedBppLogEvent> Captured = [];
    private static readonly List<CapturedStormRecovery> Recovered = [];

    internal static IReadOnlyList<CapturedBppLogEvent> Events => Captured;
    internal static IReadOnlyList<CapturedStormRecovery> Recoveries => Recovered;
    internal static IReadOnlyList<CapturedStormRecovery> StormRecoveries => Recovered;

    internal static void Reset()
    {
        Captured.Clear();
        Recovered.Clear();
    }

    [Conditional("DEBUG")]
    public static void DebugEvent(
        BppLogEventDefinition definition,
        Func<BppLogFieldValue[]> valuesFactory
    ) => Add("Debug", definition, valuesFactory(), null);

    [Conditional("DEBUG")]
    public static void DebugEvent(
        BppLogEventDefinition definition,
        Exception exception,
        Func<BppLogFieldValue[]> valuesFactory
    ) => Add("Debug", definition, valuesFactory(), exception);

    public static void InfoEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => Add("Info", definition, values, null);

    public static void WarnEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => Add("Warning", definition, values, null);

    public static void WarnEvent(
        BppLogEventDefinition definition,
        Exception exception,
        params BppLogFieldValue[] values
    ) => Add("Warning", definition, values, exception);

    public static void ErrorEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => Add("Error", definition, values, null);

    public static void ErrorEvent(
        BppLogEventDefinition definition,
        Exception exception,
        params BppLogFieldValue[] values
    ) => Add("Error", definition, values, exception);

    public static void RecoverStorm(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => Recovered.Add(new CapturedStormRecovery(definition, values));

    private static void Add(
        string severity,
        BppLogEventDefinition definition,
        BppLogFieldValue[] values,
        Exception? exception
    ) => Captured.Add(new CapturedBppLogEvent(severity, definition, values, exception));
}
