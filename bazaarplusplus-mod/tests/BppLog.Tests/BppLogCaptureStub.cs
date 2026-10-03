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

internal static class BppLog
{
    private static readonly List<CapturedBppLogEvent> Captured = [];

    internal static IReadOnlyList<CapturedBppLogEvent> Events => Captured;

    internal static void Reset() => Captured.Clear();

    [Conditional("DEBUG")]
    public static void DebugEvent(
        BppLogEventDefinition definition,
        Func<BppLogFieldValue[]> valuesFactory
    ) => Add("Debug", definition, valuesFactory(), null);

    public static void InfoEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => Add("Info", definition, values, null);

    public static void ErrorEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => Add("Error", definition, values, null);

    public static void ErrorEvent(
        BppLogEventDefinition definition,
        Exception exception,
        params BppLogFieldValue[] values
    ) => Add("Error", definition, values, exception);

    private static void Add(
        string severity,
        BppLogEventDefinition definition,
        BppLogFieldValue[] values,
        Exception? exception
    ) => Captured.Add(new CapturedBppLogEvent(severity, definition, values, exception));
}
