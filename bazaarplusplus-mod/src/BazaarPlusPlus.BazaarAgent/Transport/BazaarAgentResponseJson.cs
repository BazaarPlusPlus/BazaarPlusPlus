#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.BazaarAgent;

public static class BazaarAgentResponseJson
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new StringEnumConverter() },
    };

    public static string BuildValidationErrorBody(BazaarAgentValidationResult validation)
    {
        var code = validation.Code switch
        {
            BazaarAgentValidationCode.Invalid => "invalid",
            BazaarAgentValidationCode.StaleOrUnavailable => "stale-or-unavailable",
            BazaarAgentValidationCode.Cooldown => "cooldown",
            BazaarAgentValidationCode.Unavailable => "unavailable",
            _ => "internal",
        };
        var envelope = new Dictionary<string, object?> { ["error"] = code };
        if (validation.Details is not null)
            envelope["details"] = validation.Details;
        if (validation.Extra is not null && validation.Extra.Count > 0)
        {
            envelope["extra"] = validation.Extra;
        }
        return JsonConvert.SerializeObject(envelope, Settings);
    }
}
