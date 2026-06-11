#nullable enable
using Newtonsoft.Json;

namespace BazaarPlusPlus.ModApi.Models;

public sealed class BazaarDbSnapshotUploadRequest
{
    [JsonProperty("schema_version")]
    public int SchemaVersion { get; set; } = 2;

    [JsonProperty("snapshot")]
    public BazaarDbSnapshotMetadata Snapshot { get; set; } = new();

    [JsonProperty("player")]
    public BazaarDbSnapshotPlayer Player { get; set; } = new();

    [JsonProperty("run")]
    public BazaarDbSnapshotRun Run { get; set; } = new();

    [JsonProperty("image")]
    public BazaarDbSnapshotImage Image { get; set; } = new();

    [JsonProperty("client")]
    public BazaarDbSnapshotClientInfo Client { get; set; } = new();
}

public sealed class BazaarDbSnapshotMetadata
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("source")]
    public string Source { get; set; } = string.Empty;

    [JsonProperty("captured_at_utc")]
    public string CapturedAtUtc { get; set; } = string.Empty;
}

public sealed class BazaarDbSnapshotPlayer
{
    [JsonProperty("account_id")]
    public string AccountId { get; set; } = string.Empty;

    [JsonProperty("display_name")]
    public string? DisplayName { get; set; }

    [JsonProperty("rank")]
    public string? Rank { get; set; }

    [JsonProperty("rating")]
    public int? Rating { get; set; }

    [JsonProperty("leaderboard_position")]
    public int? LeaderboardPosition { get; set; }
}

public sealed class BazaarDbSnapshotRun
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("day")]
    public int? Day { get; set; }

    [JsonProperty("wins")]
    public int? Wins { get; set; }

    [JsonProperty("losses")]
    public int? Losses { get; set; }

    [JsonProperty("hero")]
    public BazaarDbSnapshotHero Hero { get; set; } = new();
}

public sealed class BazaarDbSnapshotHero
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }
}

public sealed class BazaarDbSnapshotImage
{
    [JsonProperty("content_type")]
    public string ContentType { get; set; } = "image/png";

    [JsonProperty("encoding")]
    public string Encoding { get; set; } = "base64";

    [JsonProperty("data_base64")]
    public string DataBase64 { get; set; } = string.Empty;
}

public sealed class BazaarDbSnapshotClientInfo
{
    [JsonProperty("submitted_at_utc")]
    public string SubmittedAtUtc { get; set; } = string.Empty;
}
