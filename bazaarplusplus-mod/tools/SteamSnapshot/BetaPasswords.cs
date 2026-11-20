using System.Text.Json;

namespace BazaarPlusPlus.SteamSnapshot;

// STEAM_BETA_PASSWORDS is one JSON line keyed by branch. Keep access codes out of diagnostics.
public sealed class BetaPasswords
{
    public const string EnvironmentVariable = "STEAM_BETA_PASSWORDS";

    private readonly Dictionary<string, string> codes;

    private BetaPasswords(Dictionary<string, string> codes) => this.codes = codes;

    public static BetaPasswords Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException(
                $"{EnvironmentVariable} is empty; a password-protected branch needs its access code"
            );
        try
        {
            return new BetaPasswords(
                JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? []
            );
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariable} must be one JSON object of branch names and access codes"
            );
        }
    }

    public string For(string branch) =>
        codes.TryGetValue(branch, out var code) && !string.IsNullOrEmpty(code)
            ? code
            : throw new InvalidOperationException(
                $"{EnvironmentVariable} has no access code for branch {branch}"
            );
}
