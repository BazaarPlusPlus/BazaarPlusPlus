#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BazaarPlusPlus.Infrastructure.Logging;

internal sealed class BppLogValueFormatter
{
    internal const int DefaultValueBudget = 256;
    internal const int FingerprintInputCharacterBudget = 1024 * 1024;

    private const int ScalarInputBudget = 4096;
    private const int ExceptionInputBudget = 16384;
    private const int DiagnosticSanitizationInputLimit = 1024 * 1024;

    private readonly BppLogRedactor _redactor;

    internal BppLogValueFormatter(BppLogRedactor redactor)
    {
        _redactor = redactor;
    }

    internal RenderedValue Render(BppLogFieldDefinition field, object? value)
    {
        if (value == null)
            return new RenderedValue("null", false);

        if (field.Privacy == BppLogFieldPrivacy.Sensitive)
            return new RenderedValue("<redacted>", false);

        string raw;
        try
        {
            raw = FormatScalar(value);
        }
        catch
        {
            return new RenderedValue("<unrenderable>", false);
        }

        try
        {
            if (field.Correlation == BppLogCorrelationPolicy.Hash)
            {
                if (raw.Length > FingerprintInputCharacterBudget)
                    return new RenderedValue("<correlation-too-long>", true);
                var hashed = EscapeAndQuote(Hash(raw, 12), DefaultValueBudget, preserveTail: false);
                return hashed;
            }

            raw = BoundHead(raw, ScalarInputBudget, out var inputTruncated);
            switch (field.Privacy)
            {
                case BppLogFieldPrivacy.LocalPath:
                    raw = _redactor.SanitizePath(raw);
                    break;
                case BppLogFieldPrivacy.RemoteUri:
                    raw = _redactor.SanitizeRemoteUri(raw);
                    break;
                case BppLogFieldPrivacy.UntrustedText:
                    raw = _redactor.SanitizeDiagnosticText(raw);
                    break;
            }

            raw = ApplyCorrelation(raw, field.Correlation);
            var rendered = EscapeAndQuote(raw, DefaultValueBudget, preserveTail: false);
            return new RenderedValue(rendered.Text, inputTruncated || rendered.Truncated);
        }
        catch
        {
            return new RenderedValue("<unrenderable>", false);
        }
    }

    internal RenderedValue RenderExceptionText(string raw, int budget, bool preserveTail)
    {
        try
        {
            if (raw.Length > DiagnosticSanitizationInputLimit)
                return new RenderedValue("<diagnostic-too-large>", true);
            var sanitized = _redactor.SanitizeDiagnosticText(raw);
            var bounded = BoundDiagnosticInput(
                sanitized,
                ExceptionInputBudget,
                preserveTail,
                out var inputTruncated
            );
            var rendered = EscapeAndQuote(bounded, budget, preserveTail);
            return new RenderedValue(rendered.Text, inputTruncated || rendered.Truncated);
        }
        catch
        {
            return new RenderedValue("<unavailable>", false);
        }
    }

    internal bool TryFingerprint(BppLogFieldDefinition field, object? value, out string fingerprint)
    {
        fingerprint = string.Empty;
        if (value == null || field.Privacy == BppLogFieldPrivacy.Sensitive)
            return false;

        try
        {
            return TryFingerprintText(FormatScalar(value), out fingerprint);
        }
        catch
        {
            return false;
        }
    }

    internal bool TryFingerprintText(string? value, out string fingerprint)
    {
        fingerprint = string.Empty;
        try
        {
            if (value == null || value.Length > FingerprintInputCharacterBudget)
                return false;
            fingerprint = Hash(value, 64);
            return fingerprint.Length == 64;
        }
        catch
        {
            fingerprint = string.Empty;
            return false;
        }
    }

    private static string FormatScalar(object value)
    {
        switch (value)
        {
            case string text:
                return text;
            case char character:
                return character.ToString();
            case bool boolean:
                return boolean ? "true" : "false";
            case DateTime timestamp:
                return FormatUtc(timestamp);
            case DateTimeOffset timestampWithOffset:
                return FormatUtc(timestampWithOffset.UtcDateTime);
            case Guid guid:
                return guid.ToString("D").ToLowerInvariant();
            case Enum enumValue:
                return ToSnakeCase(enumValue.ToString());
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture) ?? "null";
            default:
                return value.ToString() ?? "null";
        }
    }

    private static string FormatUtc(DateTime value)
    {
        DateTime utc;
        if (value.Kind == DateTimeKind.Unspecified)
            utc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        else
            utc = value.ToUniversalTime();
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    private static string ApplyCorrelation(string value, BppLogCorrelationPolicy policy)
    {
        switch (policy)
        {
            case BppLogCorrelationPolicy.None:
            case BppLogCorrelationPolicy.Full:
                return value;
            case BppLogCorrelationPolicy.Short:
                return TakeHead(value, 8);
            default:
                return "<invalid-correlation>";
        }
    }

    private static string Hash(string value, int hexCharacterCount)
    {
        using var sha256 = SHA256.Create();
        var byteBuffer = new byte[4096];
        var byteCount = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character <= 0x7F)
            {
                AppendHashByte(sha256, byteBuffer, ref byteCount, (byte)character);
            }
            else if (character <= 0x7FF)
            {
                AppendHashByte(sha256, byteBuffer, ref byteCount, (byte)(0xC0 | character >> 6));
                AppendHashByte(sha256, byteBuffer, ref byteCount, (byte)(0x80 | character & 0x3F));
            }
            else if (
                char.IsHighSurrogate(character)
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1])
            )
            {
                var codePoint = char.ConvertToUtf32(character, value[++index]);
                AppendHashByte(sha256, byteBuffer, ref byteCount, (byte)(0xF0 | codePoint >> 18));
                AppendHashByte(
                    sha256,
                    byteBuffer,
                    ref byteCount,
                    (byte)(0x80 | codePoint >> 12 & 0x3F)
                );
                AppendHashByte(
                    sha256,
                    byteBuffer,
                    ref byteCount,
                    (byte)(0x80 | codePoint >> 6 & 0x3F)
                );
                AppendHashByte(sha256, byteBuffer, ref byteCount, (byte)(0x80 | codePoint & 0x3F));
            }
            else if (char.IsSurrogate(character))
            {
                // 0xFF cannot occur in valid UTF-8, so this domain-separates malformed UTF-16
                // code units from both valid text and their printable "\\uXXXX" spellings.
                AppendHashByte(sha256, byteBuffer, ref byteCount, 0xFF);
                AppendHashByte(sha256, byteBuffer, ref byteCount, (byte)(character >> 8));
                AppendHashByte(sha256, byteBuffer, ref byteCount, (byte)(character & 0xFF));
            }
            else
            {
                AppendHashByte(sha256, byteBuffer, ref byteCount, (byte)(0xE0 | character >> 12));
                AppendHashByte(
                    sha256,
                    byteBuffer,
                    ref byteCount,
                    (byte)(0x80 | character >> 6 & 0x3F)
                );
                AppendHashByte(sha256, byteBuffer, ref byteCount, (byte)(0x80 | character & 0x3F));
            }
        }

        if (byteCount > 0)
            sha256.TransformBlock(byteBuffer, 0, byteCount, byteBuffer, 0);
        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var bytes = sha256.Hash ?? Array.Empty<byte>();
        if (
            hexCharacterCount <= 0
            || (hexCharacterCount & 1) != 0
            || bytes.Length < hexCharacterCount / 2
        )
            return "<unrenderable>";
        var result = new char[hexCharacterCount];
        const string resultHex = "0123456789abcdef";
        for (var index = 0; index < result.Length / 2; index++)
        {
            result[index * 2] = resultHex[bytes[index] >> 4];
            result[index * 2 + 1] = resultHex[bytes[index] & 0x0F];
        }
        return new string(result);
    }

    private static void AppendHashByte(HashAlgorithm hash, byte[] buffer, ref int count, byte value)
    {
        if (count == buffer.Length)
        {
            hash.TransformBlock(buffer, 0, count, buffer, 0);
            count = 0;
        }
        buffer[count++] = value;
    }

    private static RenderedValue EscapeAndQuote(string value, int budget, bool preserveTail)
    {
        var tokens = Tokenize(value);
        var quote = NeedsQuotes(value);
        var contentBudget = Math.Max(0, budget - (quote ? 2 : 0));
        var escapedLength = 0;
        for (var index = 0; index < tokens.Count; index++)
            escapedLength += tokens[index].Length;

        if (escapedLength <= contentBudget)
            return new RenderedValue(Wrap(tokens, quote), false);

        const string marker = "…";
        var selected = new System.Collections.Generic.List<string>();
        var remaining = Math.Max(0, contentBudget - marker.Length);
        if (preserveTail)
        {
            var headBudget = remaining / 2;
            var tailBudget = remaining - headBudget;
            var head = TakeTokensFromStart(tokens, headBudget);
            var tail = TakeTokensFromEnd(tokens, tailBudget);
            selected.AddRange(head);
            selected.Add(marker);
            selected.AddRange(tail);
        }
        else
        {
            selected.AddRange(TakeTokensFromStart(tokens, remaining));
            selected.Add(marker);
        }

        return new RenderedValue(Wrap(selected, quote), true);
    }

    private static System.Collections.Generic.List<string> Tokenize(string value)
    {
        var tokens = new System.Collections.Generic.List<string>(value.Length);
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

    private static string Wrap(System.Collections.Generic.IReadOnlyList<string> tokens, bool quote)
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

    private static System.Collections.Generic.List<string> TakeTokensFromStart(
        System.Collections.Generic.IReadOnlyList<string> tokens,
        int budget
    )
    {
        var result = new System.Collections.Generic.List<string>();
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

    private static System.Collections.Generic.List<string> TakeTokensFromEnd(
        System.Collections.Generic.IReadOnlyList<string> tokens,
        int budget
    )
    {
        var result = new System.Collections.Generic.List<string>();
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

    private static string BoundHead(string value, int budget, out bool truncated)
    {
        if (value.Length <= budget)
        {
            truncated = false;
            return value;
        }

        truncated = true;
        return TakeHead(value, budget);
    }

    private static string BoundDiagnosticInput(
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

        const string marker = "\n…\n";
        var remaining = Math.Max(0, budget - marker.Length);
        var headBudget = remaining / 2;
        var tailBudget = remaining - headBudget;
        var head = TakeHead(value, headBudget);
        var tailStart = Math.Max(0, value.Length - tailBudget);
        if (tailStart > 0 && char.IsLowSurrogate(value[tailStart]))
            tailStart++;
        return head + marker + value.Substring(tailStart);
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (
                char.IsUpper(character)
                && index > 0
                && value[index - 1] != '_'
                && (
                    char.IsLower(value[index - 1])
                    || (index + 1 < value.Length && char.IsLower(value[index + 1]))
                )
            )
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }
}

internal readonly struct RenderedValue
{
    internal RenderedValue(string text, bool truncated)
    {
        Text = text;
        Truncated = truncated;
    }

    internal string Text { get; }

    internal bool Truncated { get; }
}
