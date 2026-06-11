#nullable enable
using System;

namespace BazaarPlusPlus.ModApi;

public sealed class ModApiRoutes
{
    private ModApiRoutes(Uri apiBaseUri)
    {
        ApiBaseUri = apiBaseUri;
        UploadRunBundle = BuildAbsolute("/run-bundles");
        QueryGhostBattles = BuildAbsolute("/ghost-battles");
        Health = BuildAbsolute("/health");
    }

    public Uri ApiBaseUri { get; }

    public string UploadRunBundle { get; }

    public string QueryGhostBattles { get; }

    public string Health { get; }

    public string CreateReplayLink(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            throw new ArgumentException("Battle id is required.", nameof(battleId));

        return BuildAbsolute($"/ghost-battles/{Uri.EscapeDataString(battleId.Trim())}/replay-link");
    }

    public string CreateBazaarDbSnapshotUpload(string snapshotId)
    {
        if (string.IsNullOrWhiteSpace(snapshotId))
            throw new ArgumentException("Snapshot id is required.", nameof(snapshotId));

        return $"{ApiBaseUri.ToString().TrimEnd('/')}/bazaardb/snapshots/{Uri.EscapeDataString(snapshotId.Trim())}";
    }

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
