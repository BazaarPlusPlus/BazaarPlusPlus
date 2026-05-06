#nullable enable

using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Online;

internal static class V3UploadDefaults
{
    public const string DefaultApiBaseUrl = "https://api.example.com";

    private static string _apiBaseUrl = DefaultApiBaseUrl;

    public static string ApiBaseUrl => _apiBaseUrl;

    public const int StartupDelaySeconds = 20;
    public const int IntervalSeconds = 180;
    public const int RequestTimeoutSeconds = 60;

    public static void Configure(IBppConfig? config)
    {
        _apiBaseUrl = ResolveConfiguredApiBaseUrl(config?.ModApiV3BaseUrlConfig?.Value);
    }

    private static string ResolveConfiguredApiBaseUrl(string? configuredUrl)
    {
        if (string.IsNullOrWhiteSpace(configuredUrl))
            return DefaultApiBaseUrl;

        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri))
            return DefaultApiBaseUrl;

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return DefaultApiBaseUrl;

        return uri.ToString();
    }
}
