#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Infrastructure;

internal sealed record CapturedRunLoggingQueueEvent(
    string Severity,
    BppLogEventDefinition Definition,
    BppLogFieldValue[] Values,
    Exception? Exception
);

internal static class BppLog
{
    private static readonly List<CapturedRunLoggingQueueEvent> Captured = [];

    internal static IReadOnlyList<CapturedRunLoggingQueueEvent> Events => Captured;

    internal static void Reset() => Captured.Clear();

    public static void WarnEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => Captured.Add(new("Warning", definition, values, null));

    public static void ErrorEvent(
        BppLogEventDefinition definition,
        Exception exception,
        params BppLogFieldValue[] values
    ) => Captured.Add(new("Error", definition, values, exception));
}
