#nullable enable
namespace BazaarPlusPlus.Core.GameAuth;

/// <summary>Game-session values read synchronously on the host main thread before any await.</summary>
public sealed class BppGameAuthSnapshot
{
    public BppGameAuthSnapshot(string accessToken, string platform, string flavor)
    {
        AccessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        Platform = platform ?? throw new ArgumentNullException(nameof(platform));
        Flavor = flavor ?? throw new ArgumentNullException(nameof(flavor));
    }

    public string AccessToken { get; }
    public string Platform { get; }
    public string Flavor { get; }
}

public delegate BppGameAuthSnapshot? BppGameAuthReader();
