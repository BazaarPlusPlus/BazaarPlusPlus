#nullable enable
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BazaarPlusPlus.Game.RunLogging;

public static class RunIdFactory
{
    public static string Create(
        DateTimeOffset startedAtUtc,
        string hero,
        string gameMode,
        int? seed,
        string? nonce
    )
    {
        var utc = startedAtUtc.ToUniversalTime();
        var timeFragment = utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)
            .ToLowerInvariant();
        var heroFragment = Normalize(hero);
        var modeFragment = Normalize(gameMode);
        var seedFragment = seed.HasValue
            ? seed.Value.ToString("x4", CultureInfo.InvariantCulture)
            : "noseed";
        var nonceFragment = NormalizeNonce(
            nonce,
            $"{timeFragment}|{heroFragment}|{modeFragment}|{seedFragment}"
        );

        return $"run_{timeFragment}_{heroFragment}_{modeFragment}_{seedFragment}_{nonceFragment}";
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasSeparator = false;
                continue;
            }

            if (lastWasSeparator)
                continue;

            builder.Append('_');
            lastWasSeparator = true;
        }

        var normalized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }

    private static string NormalizeNonce(string? nonce, string fallbackSource)
    {
        var normalized = Normalize(nonce ?? string.Empty).Replace("_", string.Empty);
        if (!string.IsNullOrWhiteSpace(normalized))
            return normalized;

        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(fallbackSource));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));

        return builder.ToString()[..8];
    }
}
