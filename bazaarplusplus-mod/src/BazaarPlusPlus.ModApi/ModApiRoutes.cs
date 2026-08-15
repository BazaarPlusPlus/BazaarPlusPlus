#nullable enable
namespace BazaarPlusPlus.ModApi;

internal sealed class ModApiRoutes
{
    private ModApiRoutes(Uri apiBaseUri)
    {
        ApiBaseUri = apiBaseUri;
        UploadBundle = BuildAbsolute("/bundles");
        QueryGhostBattles = BuildAbsolute("/ghost-battles");
        Health = BuildAbsolute("/health");
    }

    public Uri ApiBaseUri { get; }

    public string UploadBundle { get; }

    public string QueryGhostBattles { get; }

    public string Health { get; }

    public static ModApiRoutes? TryCreate(string? apiBaseUrl)
    {
        if (
            string.IsNullOrWhiteSpace(apiBaseUrl)
            || !Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiBaseUri)
            || (apiBaseUri.Scheme != Uri.UriSchemeHttps && apiBaseUri.Scheme != Uri.UriSchemeHttp)
        )
        {
            return null;
        }

        return new ModApiRoutes(
            new UriBuilder(apiBaseUri) { Path = string.Empty, Query = string.Empty }.Uri
        );
    }

    private string BuildAbsolute(string path)
    {
        return new Uri(ApiBaseUri, path).ToString();
    }
}
