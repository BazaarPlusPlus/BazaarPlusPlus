#nullable enable
namespace BazaarPlusPlus.ManagedPayload;

// Managed Payload zips and manifests use the Tauri updater signing key. A test pins this
// key line to plugins.updater.pubkey in bazaarplusplus-installer/src-tauri/tauri.conf.json.
internal static class ReleaseSigningKey
{
    public const string PublicKeyLine = "RWTvztrqhSPg9Q3b1e0VLpJfCMxUFd5khDkrmoiSE0V8HRbfF7hJaRMZ";

    public static MinisignPublicKey PublicKey { get; } = MinisignPublicKey.Parse(PublicKeyLine);
}
