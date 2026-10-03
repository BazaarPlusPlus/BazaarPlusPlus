#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BazaarPlusPlus.Infrastructure.Logging;

internal static class BppLogValueFormatter
{
    private const int HashChunkBytes = 1024;

    // Emit can run off the main thread, so the reusable hash state is per thread. The
    // instance is deliberately never disposed: it lives for the thread's lifetime.
    [ThreadStatic]
    private static SHA256? _hashAlgorithm;

    [ThreadStatic]
    private static byte[]? _hashBuffer;

    internal static string Render(BppLogField field)
    {
        if (field.Value == null)
            return "null";

        string raw;
        try
        {
            raw = FormatScalar(field.Value);
        }
        catch
        {
            return "<unrenderable>";
        }

        try
        {
            return EscapeAndQuote(ApplyCorrelation(raw, field.Policy));
        }
        catch
        {
            return "<unrenderable>";
        }
    }

    internal static string FormatScalar(object value)
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

    internal static string EscapeAndQuote(string value)
    {
        if (!NeedsEscapeOrQuotes(value))
            return value;

        var builder = new StringBuilder(value.Length + 2);
        var quote = NeedsQuotes(value);
        if (quote)
            builder.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (
                        char.IsHighSurrogate(character)
                        && index + 1 < value.Length
                        && char.IsLowSurrogate(value[index + 1])
                    )
                    {
                        builder.Append(character).Append(value[++index]);
                    }
                    else if (
                        char.IsControl(character)
                        || character == '\u2028'
                        || character == '\u2029'
                        || char.IsSurrogate(character)
                    )
                    {
                        builder
                            .Append("\\u")
                            .Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }
        if (quote)
            builder.Append('"');
        return builder.ToString();
    }

    internal static string Hash(string value, int hexCharacterCount)
    {
        var sha256 = _hashAlgorithm ??= SHA256.Create();
        sha256.Initialize();
        var byteBuffer = _hashBuffer ??= new byte[HashChunkBytes];
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

    private static string ApplyCorrelation(string value, BppLogCorrelationPolicy policy)
    {
        switch (policy)
        {
            case BppLogCorrelationPolicy.None:
            case BppLogCorrelationPolicy.Full:
                return value;
            case BppLogCorrelationPolicy.Short:
                return TakeHead(value, 8);
            case BppLogCorrelationPolicy.Hash:
                return Hash(value, 12);
            default:
                return "<invalid-correlation>";
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

    private static void AppendHashByte(HashAlgorithm hash, byte[] buffer, ref int count, byte value)
    {
        if (count == buffer.Length)
        {
            hash.TransformBlock(buffer, 0, count, buffer, 0);
            count = 0;
        }
        buffer[count++] = value;
    }

    private static bool NeedsEscapeOrQuotes(string value)
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

            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    return true;
                index++;
                continue;
            }

            if (char.IsSurrogate(character))
                return true;
        }

        return false;
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

    private static string TakeHead(string value, int maxCharacters)
    {
        if (value.Length <= maxCharacters)
            return value;
        var length = maxCharacters;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
            length--;
        return value.Substring(0, length);
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
