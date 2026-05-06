#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using BazaarPlusPlus.Game.Online.Models;
using MessagePack;
using MessagePack.Resolvers;

namespace BazaarPlusPlus.Game.Online;

internal static class V3RunBundleArtifactCodec
{
    public const string ContentType = "application/x-bpp-runbundle+msgpack+gzip";

    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(
            ContractlessStandardResolverAllowPrivate.Instance
        );

    public static byte[] Serialize(RunArtifactV3 artifact)
    {
        if (artifact == null)
            throw new ArgumentNullException(nameof(artifact));

        var messagePackBytes = MessagePackSerializer.Serialize(artifact, Options);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(messagePackBytes, 0, messagePackBytes.Length);
        }

        return output.ToArray();
    }

    public static RunArtifactV3? Deserialize(byte[] artifactBytes)
    {
        if (artifactBytes == null || artifactBytes.Length == 0)
            return null;

        if (!LooksLikeGzip(artifactBytes))
            return null;

        try
        {
            using var input = new MemoryStream(artifactBytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            gzip.CopyTo(decompressed);
            return MessagePackSerializer.Deserialize<RunArtifactV3>(
                decompressed.ToArray(),
                Options
            );
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeGzip(byte[] bytes)
    {
        return bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;
    }
}
