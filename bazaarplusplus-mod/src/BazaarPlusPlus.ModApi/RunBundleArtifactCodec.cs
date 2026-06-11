#nullable enable
using BazaarPlusPlus.ModApi.Models;

namespace BazaarPlusPlus.ModApi;

public static class RunBundleArtifactCodec
{
    public const string ContentType = "application/x-bpp-runbundle+msgpack+gzip";

    public static byte[] Serialize(RunArtifact artifact) =>
        MessagePackGzipCodec.Serialize(artifact);

    public static bool TryDeserialize(
        byte[]? artifactBytes,
        out RunArtifact? artifact,
        out string? error
    ) => MessagePackGzipCodec.TryDeserialize(artifactBytes, out artifact, out error);
}
