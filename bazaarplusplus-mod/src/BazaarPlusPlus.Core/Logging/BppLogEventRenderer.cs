#nullable enable
using System.Text;

namespace BazaarPlusPlus.Infrastructure.Logging;

// Operational log consumers read the [BPP][Scope] event=id prefix and trailing key-value fields.
public static class BppLogEventRenderer
{
    public const string FallbackRecord = "[BPP][Logger] event=logging.render.failed";

    public static string Render(
        BppLogEvent logEvent,
        IReadOnlyList<BppLogField>? fields,
        Exception? exception
    )
    {
        try
        {
            if (!IsValidEvent(logEvent))
                return FallbackRecord;

            var builder = new StringBuilder("[BPP][")
                .Append(logEvent.Scope.PrefixName)
                .Append("] event=")
                .Append(logEvent.Id);
            for (var index = 0; index < (fields?.Count ?? 0); index++)
            {
                var field = fields![index];
                if (
                    !BppLogSchemaRules.IsSnakeIdentifier(field.Name)
                    || field.Policy < BppLogCorrelationPolicy.None
                    || field.Policy > BppLogCorrelationPolicy.Hash
                )
                    return FallbackRecord;
                builder
                    .Append(' ')
                    .Append(field.Name)
                    .Append('=')
                    .Append(BppLogValueFormatter.Render(field));
            }

            if (exception != null)
            {
                builder
                    .Append(" exception_type=")
                    .Append(BppLogValueFormatter.EscapeAndQuote(ExceptionType(exception)))
                    .Append(" exception=")
                    .Append(BppLogValueFormatter.EscapeAndQuote(ExceptionText(exception)));
            }
            return builder.ToString();
        }
        catch
        {
            return FallbackRecord;
        }
    }

    public static string ExceptionType(Exception exception)
    {
        try
        {
            var type = exception.GetType();
            return type.FullName ?? type.Name;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string ExceptionText(Exception exception)
    {
        try
        {
            return exception.ToString();
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static bool IsValidEvent(BppLogEvent logEvent)
    {
        if (!BppLogFeatureScope.IsDeclared(logEvent.Scope))
            return false;
        if (!BppLogSchemaRules.IsDottedSnakeIdentifier(logEvent.Id, minimumSegments: 3))
            return false;
        return logEvent.Id.StartsWith(logEvent.Scope.EventIdPrefix + ".", StringComparison.Ordinal);
    }
}
