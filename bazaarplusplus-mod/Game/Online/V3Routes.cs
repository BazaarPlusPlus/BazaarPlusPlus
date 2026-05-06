#nullable enable
using System;

namespace BazaarPlusPlus.Game.Online;

internal sealed class V3Routes
{
    private V3Routes(Uri apiBaseUri)
    {
        ApiBaseUri = apiBaseUri;
        UploadRunBundle = BuildAbsolute("/run-bundles");
        QueryGhostBattles = BuildAbsolute("/ghost-battles");
    }

    public Uri ApiBaseUri { get; }

    public string UploadRunBundle { get; }

    public string QueryGhostBattles { get; }

    public string CreateReplayLink(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            throw new ArgumentException("Battle id is required.", nameof(battleId));

        return BuildAbsolute($"/ghost-battles/{Uri.EscapeDataString(battleId.Trim())}/replay-link");
    }

    public string DownloadReplay(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Replay token is required.", nameof(token));

        return BuildAbsolute($"/replays/{Uri.EscapeDataString(token.Trim())}");
    }

    public static V3Routes? TryCreate(string? apiBaseUrl)
    {
        if (
            string.IsNullOrWhiteSpace(apiBaseUrl)
            || !Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiBaseUri)
            || (apiBaseUri.Scheme != Uri.UriSchemeHttps && apiBaseUri.Scheme != Uri.UriSchemeHttp)
        )
        {
            return null;
        }

        return new V3Routes(
            new UriBuilder(apiBaseUri) { Path = string.Empty, Query = string.Empty }.Uri
        );
    }

    private string BuildAbsolute(string path)
    {
        return new Uri(ApiBaseUri, path).ToString();
    }
}
