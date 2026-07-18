#nullable enable
using System.Text;

namespace BazaarPlusPlus.Infrastructure.Logging;

internal sealed class BppLogEventRenderer
{
    internal const int FieldCharacterBudget = BppLogValueFormatter.DefaultValueBudget;
    internal const int RecordCharacterBudget = 2048;
    internal const int ExceptionRecordCharacterBudget = 8192;

    private const string FallbackRecord = "[BPP][Logger] event=logging.render.failed";
    private readonly BppLogExceptionProjector _exceptionProjector;
    private readonly BppLogValueFormatter _valueFormatter;

    internal BppLogEventRenderer(BppLogRedactionRoots? roots = null)
    {
        var redactor = new BppLogRedactor(roots ?? BppLogRedactionRoots.Empty);
        _valueFormatter = new BppLogValueFormatter(redactor);
        _exceptionProjector = new BppLogExceptionProjector(_valueFormatter);
    }

    internal string Render(BppLogEventDefinition definition, params BppLogFieldValue[] values) =>
        Render(definition, values, null);

    internal string Render(
        BppLogEventDefinition definition,
        IReadOnlyList<BppLogFieldValue>? values,
        Exception? exception
    )
    {
        try
        {
            if (!IsValidDefinition(definition))
                return FallbackRecord;

            if (!TryGetOrderedFields(definition, out var orderedFields))
                return FallbackRecord;

            var fieldTokens = new List<string>();
            var fieldTruncated = false;
            for (var fieldIndex = 0; fieldIndex < orderedFields.Length; fieldIndex++)
            {
                var field = orderedFields[fieldIndex];

                if (!TryFindValue(field, values, out var value))
                    continue;

                var rendered = _valueFormatter.Render(field, value);
                fieldTokens.Add(field.Name + "=" + rendered.Text);
                fieldTruncated |= rendered.Truncated;
            }
            if (fieldTruncated)
                fieldTokens.Insert(0, "field_truncated=true");

            var exceptionTokens = new List<string>();
            var exceptionTruncated = false;
            if (exception != null)
            {
                var projection = _exceptionProjector.Project(exception);
                for (var index = 0; index < projection.Tokens.Count; index++)
                    exceptionTokens.Add(projection.Tokens[index]);
                exceptionTruncated = projection.Truncated;
            }
            if (exceptionTruncated)
                exceptionTokens.Add("exception_truncated=true");

            var prefix = "[BPP][" + definition.Scope.PrefixName + "] event=" + definition.EventId;
            var budget = exception == null ? RecordCharacterBudget : ExceptionRecordCharacterBudget;
            return exception == null
                ? Compose(prefix, fieldTokens, budget)
                : ComposeWithException(prefix, fieldTokens, exceptionTokens, budget);
        }
        catch
        {
            return FallbackRecord;
        }
    }

    internal bool TryFingerprint(BppLogFieldDefinition field, object? value, out string fingerprint)
    {
        try
        {
            return _valueFormatter.TryFingerprint(field, value, out fingerprint);
        }
        catch
        {
            fingerprint = string.Empty;
            return false;
        }
    }

    internal bool TryFingerprint(Exception? exception, out string fingerprint)
    {
        fingerprint = string.Empty;
        if (exception == null)
            return false;

        try
        {
            return _exceptionProjector.TryFingerprint(exception, out fingerprint);
        }
        catch
        {
            fingerprint = string.Empty;
            return false;
        }
    }

    private static bool IsValidDefinition(BppLogEventDefinition? definition)
    {
        if (definition == null || !BppLogFeatureScope.IsDeclared(definition.Scope))
            return false;
        if (!BppLogSchemaRules.IsDottedSnakeIdentifier(definition.EventId, minimumSegments: 3))
            return false;
        return definition.EventId.StartsWith(
            definition.Scope.EventIdPrefix + ".",
            StringComparison.Ordinal
        );
    }

    private static bool TryGetOrderedFields(
        BppLogEventDefinition definition,
        out BppLogFieldDefinition[] fields
    )
    {
        if (definition.Fields.Count > BppLogSchemaRules.MaximumFields)
        {
            fields = Array.Empty<BppLogFieldDefinition>();
            return false;
        }

        fields = new BppLogFieldDefinition[definition.Fields.Count];
        for (var index = 0; index < fields.Length; index++)
        {
            var field = definition.Fields[index];
            if (
                field == null
                || field.Order < 0
                || !BppLogSchemaRules.IsSnakeIdentifier(field.Name)
                || !BppLogSchemaRules.IsKnownPrivacy(field.Privacy)
                || !BppLogSchemaRules.IsKnownCorrelation(field.Correlation)
                || !BppLogSchemaRules.IsKnownCardinality(field.Cardinality)
            )
                return false;
            fields[index] = field;
        }

        Array.Sort(fields, (left, right) => left.Order.CompareTo(right.Order));
        for (var index = 1; index < fields.Length; index++)
        {
            if (fields[index - 1].Order == fields[index].Order)
                return false;
        }
        return true;
    }

    private static bool TryFindValue(
        BppLogFieldDefinition field,
        IReadOnlyList<BppLogFieldValue>? values,
        out object? value
    )
    {
        if (values != null)
        {
            for (var index = 0; index < values.Count; index++)
            {
                var candidate = values[index];
                if (!ReferenceEquals(candidate.Field, field))
                    continue;
                value = candidate.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string Compose(string prefix, IReadOnlyList<string> tokens, int budget)
    {
        const string truncationToken = " record_truncated=true";
        var builder = new StringBuilder(prefix);
        var truncated = false;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var reserve = index + 1 < tokens.Count ? truncationToken.Length : 0;
            if (builder.Length + 1 + token.Length + reserve > budget)
            {
                truncated = true;
                break;
            }
            builder.Append(' ').Append(token);
        }

        if (truncated && builder.Length + truncationToken.Length <= budget)
            builder.Append(truncationToken);
        return builder.Length <= budget ? builder.ToString() : FallbackRecord;
    }

    private static string ComposeWithException(
        string prefix,
        IReadOnlyList<string> fieldTokens,
        IReadOnlyList<string> exceptionTokens,
        int budget
    )
    {
        const string truncationToken = " record_truncated=true";
        var exceptionLength = 0;
        for (var index = 0; index < exceptionTokens.Count; index++)
            exceptionLength += 1 + exceptionTokens[index].Length;
        if (prefix.Length + exceptionLength > budget)
            return FallbackRecord;

        var builder = new StringBuilder(prefix);
        var fieldsTruncated = false;
        for (var index = 0; index < fieldTokens.Count; index++)
        {
            var token = fieldTokens[index];
            var hasMoreFields = index + 1 < fieldTokens.Count;
            var markerReserve = hasMoreFields ? truncationToken.Length : 0;
            if (builder.Length + 1 + token.Length + markerReserve + exceptionLength > budget)
            {
                fieldsTruncated = true;
                break;
            }
            builder.Append(' ').Append(token);
        }

        if (fieldsTruncated)
            builder.Append(truncationToken);
        for (var index = 0; index < exceptionTokens.Count; index++)
            builder.Append(' ').Append(exceptionTokens[index]);
        return builder.Length <= budget ? builder.ToString() : FallbackRecord;
    }
}
