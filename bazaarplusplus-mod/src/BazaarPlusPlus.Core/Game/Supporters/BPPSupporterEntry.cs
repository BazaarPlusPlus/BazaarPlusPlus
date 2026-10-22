#nullable enable
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.Supporters;

public sealed class BPPSupporterEntry
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("tier")]
    public int Tier { get; set; }
}
