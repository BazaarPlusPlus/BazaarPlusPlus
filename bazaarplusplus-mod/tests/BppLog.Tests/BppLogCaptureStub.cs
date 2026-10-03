#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Infrastructure;

internal sealed record CapturedBppLogEvent(
    string Severity,
    BppLogEvent Event,
    BppLogField[] Fields,
    Exception? Exception
);

internal static class BppLog
{
    private static readonly List<CapturedBppLogEvent> Captured = [];

    internal static IReadOnlyList<CapturedBppLogEvent> Events => Captured;

    internal static void Reset() => Captured.Clear();

    [Conditional("DEBUG")]
    public static void DebugEvent(BppLogEvent logEvent, Func<BppLogField[]> fieldsFactory) =>
        Add("Debug", logEvent, fieldsFactory(), null);

    public static void InfoEvent(BppLogEvent logEvent, params BppLogField[] fields) =>
        Add("Info", logEvent, fields, null);

    public static void ErrorEvent(BppLogEvent logEvent, params BppLogField[] fields) =>
        Add("Error", logEvent, fields, null);

    public static void ErrorEvent(
        BppLogEvent logEvent,
        Exception exception,
        params BppLogField[] fields
    ) => Add("Error", logEvent, fields, exception);

    private static void Add(
        string severity,
        BppLogEvent logEvent,
        BppLogField[] fields,
        Exception? exception
    ) => Captured.Add(new CapturedBppLogEvent(severity, logEvent, fields, exception));
}
