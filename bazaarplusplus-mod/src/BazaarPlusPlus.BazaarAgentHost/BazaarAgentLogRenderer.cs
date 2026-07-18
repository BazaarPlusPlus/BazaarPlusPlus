#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BazaarPlusPlus.BazaarAgent;

namespace BazaarPlusPlus.BazaarAgentHost;

internal sealed class BazaarAgentLogRenderer
{
    internal const int FieldCharacterBudget = 256;
    internal const int RecordCharacterBudget = 2048;
    internal const int ExceptionRecordCharacterBudget = 8192;

    private const int ScalarInputCharacterBudget = 4096;
    private const int ExceptionInputCharacterBudget = 16384;
    private const int ExceptionMessageBudget = 512;
    private const int InnerExceptionMessageBudget = 256;
    private const int ExceptionTypeBudget = 128;
    private const int ExceptionStackBudget = 5000;
    private const string FallbackRecord = "[BazaarAgent] event=agent.render.failed";
    private const string RecordTruncationToken = " record_truncated=true";

    private static readonly Regex RemoteUriPattern = new(
        @"https?://(?:[^\s\""'<>?#,;=]+[?#][^\r\n]*|[^\s\""'<>?#,;=]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );
    private static readonly Regex BodySensitivePattern = new(
        @"(?<![\p{L}\p{N}_])[\""']?(?:headers?|request[-_ ]?body|response[-_ ]?body)[\""']?\s*[:=][\s\S]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );
    private static readonly Regex LineSensitivePattern = new(
        @"(?<![\p{L}\p{N}_])[\""']?(?:authorization|proxy[-_ ]?authorization|cookie|set[-_ ]?cookie)[\""']?\s*[:=]\s*[^\r\n]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );
    private static readonly Regex SensitiveAssignmentPattern = new(
        @"(?<![\p{L}\p{N}_])[\""']?(?:access[-_ ]?token|refresh[-_ ]?token|token|api[-_ ]?key|password|secret|account[-_ ]?id|user[-_ ]?name|username|display[-_ ]?name|link[-_ ]?code)[\""']?\s*[:=]\s*(?:(?:bearer|basic)\s+)?(?:\""(?:\\.|[^\""\\\r\n])*\""|'(?:\\.|[^'\\\r\n])*'|[^\s,;\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );
    private static readonly Regex CredentialPattern = new(
        @"\b(?:(?:bearer|basic)\s+[^\s,;\r\n]+|eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );
    private static readonly Regex UnknownAbsolutePathPattern = new(
        @"(?<![\p{L}\p{N}<>])(?:[A-Za-z]:[\\/]|\\{1,2}|/)[^\r\n]*",
        RegexOptions.CultureInvariant
    );
    private static readonly Regex SlashProsePattern = new(
        @"(?<=[\u3400-\u4DBF\u4E00-\u9FFF\u3040-\u30FF\uAC00-\uD7AF])(?:/|[ \t]+/[ \t]+)(?=[\u3400-\u4DBF\u4E00-\u9FFF\u3040-\u30FF\uAC00-\uD7AF]+(?:(?![/\\])[\p{P}\s]|$))",
        RegexOptions.CultureInvariant
    );

    internal string Render(BazaarAgentLogEvent logEvent)
    {
        try
        {
            if (logEvent == null || !IsDottedSnakeIdentifier(logEvent.Definition.EventId))
                return FallbackRecord;

            var fieldTokens = new List<string>();
            var fieldTruncated = false;
            foreach (var field in logEvent.Definition.Fields)
            {
                if (!TryFindValue(logEvent.Values, field, out var value))
                    continue;
                var rendered = RenderValue(field, value);
                fieldTokens.Add(field.Name + "=" + rendered.Text);
                fieldTruncated |= rendered.Truncated;
            }
            if (fieldTruncated)
                fieldTokens.Insert(0, "field_truncated=true");

            var exceptionTokens = new List<string>();
            if (logEvent.Exception is { } exception)
            {
                ProjectException(exception, exceptionTokens, out var exceptionTruncated);
                if (exceptionTruncated)
                    exceptionTokens.Add("exception_truncated=true");
            }

            var prefix = "[BazaarAgent] event=" + logEvent.Definition.EventId;
            return logEvent.Exception == null
                ? Compose(prefix, fieldTokens, RecordCharacterBudget)
                : ComposeWithException(
                    prefix,
                    fieldTokens,
                    exceptionTokens,
                    ExceptionRecordCharacterBudget
                );
        }
        catch
        {
            return FallbackRecord;
        }
    }

    private static RenderedValue RenderValue(BazaarAgentLogFieldDefinition field, object? value)
    {
        if (value == null)
            return new RenderedValue("null", truncated: false);

        try
        {
            var raw = FormatScalar(value);
            if (field.Correlation == BazaarAgentLogCorrelation.Short)
                raw = TakeHead(raw, 8);
            if (field.Privacy == BazaarAgentLogFieldPrivacy.UntrustedText)
            {
                return RenderDiagnosticText(
                    raw,
                    FieldCharacterBudget,
                    preserveTail: false,
                    ScalarInputCharacterBudget
                );
            }
            return EscapeAndBound(
                raw,
                FieldCharacterBudget,
                preserveTail: false,
                ScalarInputCharacterBudget
            );
        }
        catch
        {
            return new RenderedValue("<unrenderable>", truncated: false);
        }
    }

    private static string FormatScalar(object value) =>
        value switch
        {
            string text => text,
            char character => character.ToString(),
            bool boolean => boolean ? "true" : "false",
            Enum enumValue => ToSnakeCase(enumValue.ToString()),
            DateTime timestamp => FormatUtc(timestamp),
            DateTimeOffset timestamp => FormatUtc(timestamp.UtcDateTime),
            Guid guid => guid.ToString("D").ToLowerInvariant(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture)
                ?? "null",
            _ => value.ToString() ?? "null",
        };

    private static string FormatUtc(DateTime value)
    {
        var utc =
            value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    private static void ProjectException(
        Exception exception,
        List<string> tokens,
        out bool truncated
    )
    {
        truncated = false;
        AppendException(tokens, exception, "exception", ExceptionMessageBudget, ref truncated);

        var current = SafeInnerException(exception);
        for (var depth = 1; depth <= 3 && current != null; depth++)
        {
            AppendException(
                tokens,
                current,
                "exception_inner_" + depth.ToString(CultureInfo.InvariantCulture),
                InnerExceptionMessageBudget,
                ref truncated
            );
            current = SafeInnerException(current);
        }
        if (current != null)
            truncated = true;

        var stack = SafeStackTrace(exception, out var stackUnavailable);
        if (stackUnavailable)
        {
            tokens.Add("exception_stack=<unavailable>");
        }
        else if (stack == null)
        {
            tokens.Add("exception_stack=null");
        }
        else
        {
            var rendered = RenderDiagnosticText(
                stack,
                ExceptionStackBudget,
                preserveTail: true,
                ExceptionInputCharacterBudget
            );
            tokens.Add("exception_stack=" + rendered.Text);
            truncated |= rendered.Truncated;
        }
    }

    private static void AppendException(
        List<string> tokens,
        Exception exception,
        string prefix,
        int messageBudget,
        ref bool truncated
    )
    {
        var type = EscapeAndBound(
            SafeTypeName(exception),
            ExceptionTypeBudget,
            preserveTail: false,
            ExceptionInputCharacterBudget
        );
        tokens.Add(prefix + "_type=" + type.Text);
        truncated |= type.Truncated;
        tokens.Add(prefix + "_hresult=" + SafeHResult(exception));

        var message = SafeMessage(exception, out var messageUnavailable);
        if (messageUnavailable)
        {
            tokens.Add(prefix + "_message=<unavailable>");
            return;
        }

        var rendered = RenderDiagnosticText(
            message ?? string.Empty,
            messageBudget,
            preserveTail: false,
            ExceptionInputCharacterBudget
        );
        tokens.Add(prefix + "_message=" + rendered.Text);
        truncated |= rendered.Truncated;
    }

    private static string SafeTypeName(Exception exception)
    {
        try
        {
            return exception.GetType().FullName ?? exception.GetType().Name;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string SafeHResult(Exception exception)
    {
        try
        {
            return "0x"
                + unchecked((uint)exception.HResult).ToString("X8", CultureInfo.InvariantCulture);
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string? SafeMessage(Exception exception, out bool unavailable)
    {
        try
        {
            unavailable = false;
            return exception.Message;
        }
        catch
        {
            unavailable = true;
            return null;
        }
    }

    private static Exception? SafeInnerException(Exception exception)
    {
        try
        {
            return exception.InnerException;
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeStackTrace(Exception exception, out bool unavailable)
    {
        try
        {
            unavailable = false;
            return exception.StackTrace;
        }
        catch
        {
            unavailable = true;
            return null;
        }
    }

    private static RenderedValue RenderDiagnosticText(
        string value,
        int budget,
        bool preserveTail,
        int inputBudget
    )
    {
        var bounded = BoundInput(value, inputBudget, preserveTail, out var inputTruncated);
        var sanitized = SanitizeDiagnosticText(bounded);
        return EscapeAndBound(sanitized, budget, preserveTail, inputBudget, inputTruncated);
    }

    private static string SanitizeDiagnosticText(string value)
    {
        try
        {
            var sanitized = BodySensitivePattern.Replace(value, "sensitive=<redacted>");
            sanitized = LineSensitivePattern.Replace(sanitized, "sensitive=<redacted>");
            sanitized = SensitiveAssignmentPattern.Replace(sanitized, "sensitive=<redacted>");
            sanitized = CredentialPattern.Replace(sanitized, "<redacted>");

            var remoteUris = new List<string>();
            sanitized = RemoteUriPattern.Replace(
                sanitized,
                match =>
                {
                    var marker = "BPPAGENTURIMARKER" + remoteUris.Count + "END";
                    remoteUris.Add(SanitizeRemoteUri(match.Value));
                    return marker;
                }
            );

            var slashProse = new List<string>();
            sanitized = SlashProsePattern.Replace(
                sanitized,
                match =>
                {
                    var marker = "BPPAGENTSLASHMARKER" + slashProse.Count + "END";
                    slashProse.Add(match.Value);
                    return marker;
                }
            );
            sanitized = UnknownAbsolutePathPattern.Replace(sanitized, "<absolute-path>");
            for (var index = 0; index < remoteUris.Count; index++)
            {
                sanitized = sanitized.Replace(
                    "BPPAGENTURIMARKER" + index + "END",
                    remoteUris[index]
                );
            }
            for (var index = 0; index < slashProse.Count; index++)
            {
                sanitized = sanitized.Replace(
                    "BPPAGENTSLASHMARKER" + index + "END",
                    slashProse[index]
                );
            }
            return sanitized;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string SanitizeRemoteUri(string rawUri)
    {
        try
        {
            var queryIndex = rawUri.IndexOf('?');
            var fragmentIndex = rawUri.IndexOf('#');
            var sensitiveSuffixIndex =
                queryIndex < 0 ? fragmentIndex
                : fragmentIndex < 0 ? queryIndex
                : Math.Min(queryIndex, fragmentIndex);
            var queryFree =
                sensitiveSuffixIndex < 0 ? rawUri : rawUri.Substring(0, sensitiveSuffixIndex);
            queryFree = queryFree.TrimEnd('.', ',', ';', ':', ')', ']', '}');

            if (!Uri.TryCreate(queryFree, UriKind.Absolute, out var uri))
                return "<invalid-url>";
            if (
                !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return "<invalid-url>";

            var builder = new UriBuilder(uri)
            {
                UserName = string.Empty,
                Password = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty,
            };
            return builder.Uri.GetLeftPart(UriPartial.Path);
        }
        catch
        {
            return "<invalid-url>";
        }
    }

    private static RenderedValue EscapeAndBound(
        string value,
        int budget,
        bool preserveTail,
        int inputBudget,
        bool alreadyTruncated = false
    )
    {
        var bounded = BoundInput(value, inputBudget, preserveTail, out var inputTruncated);
        inputTruncated |= alreadyTruncated;
        var tokens = Tokenize(bounded);
        var quote = NeedsQuotes(bounded);
        var contentBudget = Math.Max(0, budget - (quote ? 2 : 0));
        var escapedLength = 0;
        for (var index = 0; index < tokens.Count; index++)
            escapedLength += tokens[index].Length;

        if (!inputTruncated && escapedLength <= contentBudget)
            return new RenderedValue(Wrap(tokens, quote), truncated: false);

        const string marker = "…";
        var selected = new List<string>();
        var remaining = Math.Max(0, contentBudget - marker.Length);
        if (preserveTail)
        {
            var headBudget = remaining / 2;
            selected.AddRange(TakeTokensFromStart(tokens, headBudget));
            selected.Add(marker);
            selected.AddRange(TakeTokensFromEnd(tokens, remaining - headBudget));
        }
        else
        {
            selected.AddRange(TakeTokensFromStart(tokens, remaining));
            selected.Add(marker);
        }

        return new RenderedValue(Wrap(selected, quote), truncated: true);
    }

    private static string BoundInput(
        string value,
        int budget,
        bool preserveTail,
        out bool truncated
    )
    {
        if (value.Length <= budget)
        {
            truncated = false;
            return value;
        }

        truncated = true;
        if (!preserveTail)
            return TakeHead(value, budget);
        var headBudget = budget / 2;
        return TakeHead(value, headBudget) + TakeTail(value, budget - headBudget);
    }

    private static List<string> Tokenize(string value)
    {
        var tokens = new List<string>(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '\\':
                    tokens.Add("\\\\");
                    break;
                case '"':
                    tokens.Add("\\\"");
                    break;
                case '\r':
                    tokens.Add("\\r");
                    break;
                case '\n':
                    tokens.Add("\\n");
                    break;
                case '\t':
                    tokens.Add("\\t");
                    break;
                default:
                    if (
                        char.IsHighSurrogate(character)
                        && index + 1 < value.Length
                        && char.IsLowSurrogate(value[index + 1])
                    )
                    {
                        tokens.Add(new string(new[] { character, value[++index] }));
                    }
                    else if (
                        char.IsControl(character)
                        || character == '\u2028'
                        || character == '\u2029'
                        || char.IsSurrogate(character)
                    )
                    {
                        tokens.Add(
                            "\\u" + ((int)character).ToString("X4", CultureInfo.InvariantCulture)
                        );
                    }
                    else
                    {
                        tokens.Add(character.ToString());
                    }
                    break;
            }
        }
        return tokens;
    }

    private static bool NeedsQuotes(string value)
    {
        if (value.Length == 0)
            return true;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (
                char.IsWhiteSpace(character)
                || char.IsControl(character)
                || character == '"'
                || character == '\\'
                || character == '='
            )
                return true;
        }
        return false;
    }

    private static string Wrap(IReadOnlyList<string> tokens, bool quote)
    {
        var builder = new StringBuilder();
        if (quote)
            builder.Append('"');
        for (var index = 0; index < tokens.Count; index++)
            builder.Append(tokens[index]);
        if (quote)
            builder.Append('"');
        return builder.ToString();
    }

    private static List<string> TakeTokensFromStart(IReadOnlyList<string> tokens, int budget)
    {
        var result = new List<string>();
        var used = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (used + token.Length > budget)
                break;
            result.Add(token);
            used += token.Length;
        }
        return result;
    }

    private static List<string> TakeTokensFromEnd(IReadOnlyList<string> tokens, int budget)
    {
        var result = new List<string>();
        var used = 0;
        for (var index = tokens.Count - 1; index >= 0; index--)
        {
            var token = tokens[index];
            if (used + token.Length > budget)
                break;
            result.Insert(0, token);
            used += token.Length;
        }
        return result;
    }

    private static string TakeHead(string value, int maxCharacters)
    {
        if (value.Length <= maxCharacters)
            return value;
        var length = maxCharacters;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
            length--;
        return value.Substring(0, length);
    }

    private static string TakeTail(string value, int maxCharacters)
    {
        if (value.Length <= maxCharacters)
            return value;
        var start = value.Length - maxCharacters;
        if (start < value.Length && char.IsLowSurrogate(value[start]))
            start++;
        return value.Substring(start);
    }

    private static string Compose(string prefix, IReadOnlyList<string> tokens, int budget)
    {
        var builder = new StringBuilder(prefix);
        var truncated = false;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var reserve = index + 1 < tokens.Count ? RecordTruncationToken.Length : 0;
            if (builder.Length + 1 + token.Length + reserve > budget)
            {
                truncated = true;
                break;
            }
            builder.Append(' ').Append(token);
        }

        if (truncated && builder.Length + RecordTruncationToken.Length <= budget)
            builder.Append(RecordTruncationToken);
        return builder.Length <= budget ? builder.ToString() : FallbackRecord;
    }

    private static string ComposeWithException(
        string prefix,
        IReadOnlyList<string> fieldTokens,
        IReadOnlyList<string> exceptionTokens,
        int budget
    )
    {
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
            var reserve = index + 1 < fieldTokens.Count ? RecordTruncationToken.Length : 0;
            if (builder.Length + 1 + token.Length + reserve + exceptionLength > budget)
            {
                fieldsTruncated = true;
                break;
            }
            builder.Append(' ').Append(token);
        }

        if (
            fieldsTruncated
            && builder.Length + RecordTruncationToken.Length + exceptionLength <= budget
        )
            builder.Append(RecordTruncationToken);
        for (var index = 0; index < exceptionTokens.Count; index++)
            builder.Append(' ').Append(exceptionTokens[index]);
        return builder.Length <= budget ? builder.ToString() : FallbackRecord;
    }

    private static bool TryFindValue(
        IReadOnlyList<BazaarAgentLogFieldValue> values,
        BazaarAgentLogFieldDefinition field,
        out object? value
    )
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (!ReferenceEquals(values[index].Field, field))
                continue;
            value = values[index].Value;
            return true;
        }
        value = null;
        return false;
    }

    private static bool IsDottedSnakeIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        var segments = value.Split('.');
        if (segments.Length < 3)
            return false;
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment[0] < 'a' || segment[0] > 'z')
                return false;
            for (var index = 1; index < segment.Length; index++)
            {
                var character = segment[index];
                if (
                    (character < 'a' || character > 'z')
                    && (character < '0' || character > '9')
                    && character != '_'
                )
                    return false;
            }
        }
        return true;
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character) && index > 0)
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    private readonly struct RenderedValue
    {
        internal RenderedValue(string text, bool truncated)
        {
            Text = text;
            Truncated = truncated;
        }

        internal string Text { get; }
        internal bool Truncated { get; }
    }
}
