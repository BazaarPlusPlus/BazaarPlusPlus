using System.Text.Json;

namespace BazaarPlusPlus.SteamSnapshot;

/// <summary>
/// The access codes of password-protected Steam branches, keyed by branch: one line of JSON, the
/// value of STEAM_BETA_PASSWORDS. Messages never repeat a code.
/// </summary>
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
