#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.Storage;

public static class SerializerSettingsFactory
{
    public static JsonSerializerSettings CreateSerializerSettings(bool includeStringEnumConverter)
    {
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy(),
            },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            DateFormatString = "yyyy-MM-dd'T'HH:mm:ss.fffK",
        };

        if (includeStringEnumConverter)
            settings.Converters.Add(new StringEnumConverter());

        return settings;
    }
}
