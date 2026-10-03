#nullable enable
using Newtonsoft.Json;

namespace BazaarPlusPlus.ModApi.Models;

internal sealed class BazaarDbProfileLinkRedeemRequest
{
    [JsonProperty("code")]
    public string Code { get; set; } = string.Empty;

    [JsonProperty("account_id")]
    public string AccountId { get; set; } = string.Empty;
}
