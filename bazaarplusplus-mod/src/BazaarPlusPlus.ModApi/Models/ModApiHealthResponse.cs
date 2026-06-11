#nullable enable
using Newtonsoft.Json;

namespace BazaarPlusPlus.ModApi.Models;

public sealed class ModApiHealthResponse
{
    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("server_time_utc")]
    public string ServerTimeUtc { get; set; } = string.Empty;
}
