#nullable enable
namespace BazaarPlusPlus.Infrastructure.SelfUpdate;

/// <summary>
/// One platform's published Managed Payload (root CONTEXT.md) and the release facts self-update
/// compares against the running copy, as a Platform Release Manifest publishes them.
/// </summary>
public sealed record ManagedPayloadRelease(
    string Version,
    Uri Url,
    long Size,
    string Sha256,
    string Signature,
    int HistoryDatabaseUserVersion,
    string NonManagedPayloadDigest
);
