#nullable enable
namespace BazaarPlusPlus.Infrastructure.SelfUpdate;

public sealed record ManagedPayloadChunkIndexReference(
    Uri Url,
    long Size,
    string Sha256,
    string Signature,
    Uri ChunksBaseUrl
);

public sealed record ManagedPayloadRelease(
    string Version,
    Uri Url,
    long Size,
    string Sha256,
    string Signature,
    int HistoryDatabaseUserVersion,
    string NonManagedPayloadDigest,
    ManagedPayloadChunkIndexReference? ChunkIndex = null
);
