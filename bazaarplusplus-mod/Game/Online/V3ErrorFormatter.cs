#nullable enable
using System;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.Online;

internal static class V3ErrorFormatter
{
    public static string Truncate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "empty_response";

        return value.Length <= 256 ? value : value[..256];
    }

    public static string FormatHttpFailure(int statusCode, string responseBody)
    {
        var parsed = TryParseHttpErrorPayload(responseBody);
        if (parsed == null)
            return $"http_{statusCode}:{Truncate(responseBody)}";

        var error = parsed.Value.Error;
        var detail = parsed.Value.Detail;
        if (string.IsNullOrWhiteSpace(detail))
            return $"http_{statusCode}:{error}";

        return $"http_{statusCode}:{error}({detail})";
    }

    private static (string Error, string? Detail)? TryParseHttpErrorPayload(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            var payload = JObject.Parse(responseBody);
            var error = payload["error"]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(error))
                return null;

            var detail =
                payload["reason"]?.Value<string>()?.Trim()
                ?? payload["detail"]?.Value<string>()?.Trim()
                ?? payload["message"]?.Value<string>()?.Trim();
            return (error, string.IsNullOrWhiteSpace(detail) ? null : Truncate(detail));
        }
        catch
        {
            return null;
        }
    }
}
