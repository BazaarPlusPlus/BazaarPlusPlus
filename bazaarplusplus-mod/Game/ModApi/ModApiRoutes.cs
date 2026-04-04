#nullable enable
using System;

namespace BazaarPlusPlus.Game.ModApi;

internal sealed class ModApiRoutes
{
    private ModApiRoutes(Uri apiBaseUri)
    {
        ApiBaseUri = apiBaseUri;
        RegisterClient = BuildAbsolute("/clients/register");
        BindPlayer = BuildAbsolute("/clients/bind-player");
        UploadRunSummary = BuildAbsolute("/runs");
        UploadBattleArtifact = BuildAbsolute("/battles");
        QueryGhostBattles = BuildAbsolute("/players/me/ghost-battles");
    }

    public Uri ApiBaseUri { get; }

    public string RegisterClient { get; }

    public string BindPlayer { get; }

    public string UploadRunSummary { get; }

    public string UploadBattleArtifact { get; }

    public string QueryGhostBattles { get; }

    public string CreateReplayLink(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            throw new ArgumentException("Battle id is required.", nameof(battleId));

        return BuildAbsolute(
            $"/players/me/ghost-battles/{Uri.EscapeDataString(battleId.Trim())}/replay-link"
        );
    }

    public string DownloadReplay(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Replay token is required.", nameof(token));

        return BuildAbsolute($"/replays/{Uri.EscapeDataString(token.Trim())}");
    }

    public static ModApiRoutes? TryCreate(string? apiBaseUrl)
    {
        if (
            string.IsNullOrWhiteSpace(apiBaseUrl)
            || !Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiBaseUri)
            || !IsSupportedScheme(apiBaseUri)
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

    private static bool IsSupportedScheme(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }
}
