using System.Text.Json;

namespace BazaarPlusPlus.SteamSnapshot;

/// <summary>
/// The saved Steam login: one line of JSON, the value of STEAM_SNAPSHOT_LOGIN. Messages never
/// repeat its content.
/// </summary>
public sealed record SteamLogin(string Account, string RefreshToken)
{
    public const string EnvironmentVariable = "STEAM_SNAPSHOT_LOGIN";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static SteamLogin Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException(
                $"{EnvironmentVariable} is empty; run 'just mod::steam-login'"
            );
        SteamLogin? login;
        try
        {
            login = JsonSerializer.Deserialize<SteamLogin>(json, JsonOptions);
        }
        catch (JsonException)
        {
            login = null;
        }
        if (string.IsNullOrEmpty(login?.Account) || string.IsNullOrEmpty(login.RefreshToken))
            throw new InvalidOperationException(
                $"{EnvironmentVariable} is not a saved login; run 'just mod::steam-login'"
            );
        return login;
    }
}
