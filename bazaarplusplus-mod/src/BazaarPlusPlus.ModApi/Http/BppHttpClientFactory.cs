#nullable enable
using System.Net.Http.Headers;

namespace BazaarPlusPlus.ModApi.Http;

public static class BppHttpClientFactory
{
    private const string ProductName = "BazaarPlusPlus";

    // The backend groups metrics and rate limits by owner-specific user-agent suffix.
    // The user-agent comment carries the release platform key independently of the Run segment.

    public static HttpClient Create(
        string productVersion,
        string? platformKey,
        string? userAgentSuffix = null,
        TimeSpan? timeout = null,
        HttpMessageHandler? handler = null
    )
    {
        var client = handler == null ? new HttpClient() : new HttpClient(handler);
        if (timeout.HasValue)
            client.Timeout = timeout.Value;

        ApplyUserAgent(client, productVersion, platformKey, userAgentSuffix);
        return client;
    }

    private static void ApplyUserAgent(
        HttpClient client,
        string productVersion,
        string? platformKey,
        string? userAgentSuffix
    )
    {
        var version = SanitizeUserAgentToken(productVersion) ?? "0.0.0";
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(ProductName, version)
        );

        var platform = SanitizeUserAgentToken(platformKey);
        if (platform != null)
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("(" + platform + ")")
            );

        var sanitizedSuffix = SanitizeUserAgentToken(userAgentSuffix);
        if (sanitizedSuffix != null)
        {
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(sanitizedSuffix, version)
            );
        }
    }

    private static string? SanitizeUserAgentToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // ProductInfoHeaderValue rejects characters outside the RFC 7230 token set.
        var trimmed = value.Trim();
        var buffer = new char[trimmed.Length];
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            buffer[i] = IsTokenChar(c) ? c : '-';
        }

        return new string(buffer);
    }

    private static bool IsTokenChar(char c)
    {
        if (c >= 'A' && c <= 'Z')
            return true;
        if (c >= 'a' && c <= 'z')
            return true;
        if (c >= '0' && c <= '9')
            return true;
        switch (c)
        {
            case '!':
            case '#':
            case '$':
            case '%':
            case '&':
            case '\'':
            case '*':
            case '+':
            case '-':
            case '.':
            case '^':
            case '_':
            case '`':
            case '|':
            case '~':
                return true;
            default:
                return false;
        }
    }
}
