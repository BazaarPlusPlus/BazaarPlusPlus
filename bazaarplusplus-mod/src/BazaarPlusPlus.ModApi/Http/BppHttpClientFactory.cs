#nullable enable
using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace BazaarPlusPlus.ModApi.Http;

public static class BppHttpClientFactory
{
    private const string ProductName = "BazaarPlusPlus";

    public static HttpClient Create(
        string productVersion,
        string? userAgentSuffix = null,
        TimeSpan? timeout = null
    )
    {
        var client = new HttpClient();
        if (timeout.HasValue)
            client.Timeout = timeout.Value;

        ApplyUserAgent(client, productVersion, userAgentSuffix);
        return client;
    }

    private static void ApplyUserAgent(
        HttpClient client,
        string productVersion,
        string? userAgentSuffix
    )
    {
        var version = SanitizeUserAgentToken(productVersion) ?? "0.0.0";
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(ProductName, version)
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

        // RFC 7230 token chars; replace anything else with '-' so a flaky version string never
        // throws inside ProductInfoHeaderValue during plugin startup.
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
