#nullable enable
namespace BazaarPlusPlus.ManagedPayload;

/// <summary>
/// The one release signing key (root docs/release.md): the Tauri updater's minisign key, which
/// also signs every Managed Payload zip and Managed Payload Manifest. This is the key line of
/// the <c>plugins.updater.pubkey</c> in bazaarplusplus-installer/src-tauri/tauri.conf.json; a
/// test pins the two together.
/// </summary>
internal static class ReleaseSigningKey
{
    public const string PublicKeyLine = "RWTvztrqhSPg9Q3b1e0VLpJfCMxUFd5khDkrmoiSE0V8HRbfF7hJaRMZ";

    public static MinisignPublicKey PublicKey { get; } = MinisignPublicKey.Parse(PublicKeyLine);
}
