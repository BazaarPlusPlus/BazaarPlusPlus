using System.Text.Json;
using SteamKit2;

namespace BazaarPlusPlus.SteamSnapshot;

// STEAM_SNAPSHOT_LOGIN is a single JSON line. Keep its contents out of diagnostics.
public sealed record SteamLogin(string Account, string RefreshToken)
{
    public const string EnvironmentVariable = "STEAM_SNAPSHOT_LOGIN";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string DescribeFailure(EResult result) =>
        result
            is EResult.InvalidPassword
                or EResult.AccessDenied
                or EResult.Expired
                or EResult.Revoked
                or EResult.InvalidSignature
                or EResult.IllegalPassword
                or EResult.AccountNotFound
            ? $"Steam rejected the saved login ({result}); run 'just mod::steam-login', then copy it to the steam Environment"
            : $"Steam could not sign in this time ({result}); the saved login may still be good";

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
