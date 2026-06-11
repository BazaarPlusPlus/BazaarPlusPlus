#nullable enable
using System;
using System.Net.Http;
using System.Text;
using BazaarPlusPlus.ModApi.Models;
using Newtonsoft.Json;

namespace BazaarPlusPlus.ModApi.Http;

public static class RunBundleMultipartContent
{
    private const string ArtifactFileName = "run-bundle.mpack.gz";

    public static MultipartFormDataContent Create(
        RunBundleUploadRequest metadata,
        byte[] artifactBytes
    )
    {
        if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));
        if (artifactBytes == null || artifactBytes.Length == 0)
            throw new ArgumentException("Artifact bytes are required.", nameof(artifactBytes));

        var content = new MultipartFormDataContent();
        var metadataJson = JsonConvert.SerializeObject(
            metadata,
            ModApiSerialization.SerializerSettings
        );
        var metadataContent = new StringContent(metadataJson, Encoding.UTF8, "application/json");
        content.Add(metadataContent, "metadata");

        var artifactContent = new ByteArrayContent(artifactBytes);
        artifactContent.Headers.ContentType = new(RunBundleArtifactCodec.ContentType);
        content.Add(artifactContent, "artifact", ArtifactFileName);

        return content;
    }
}
