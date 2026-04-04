#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.Game.ModApi;

internal static class ModApiSerialization
{
    public static JsonSerializerSettings SerializerSettings { get; } =
        new()
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy(),
            },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            DateFormatString = "yyyy-MM-dd'T'HH:mm:ss.fffK",
        };
}
