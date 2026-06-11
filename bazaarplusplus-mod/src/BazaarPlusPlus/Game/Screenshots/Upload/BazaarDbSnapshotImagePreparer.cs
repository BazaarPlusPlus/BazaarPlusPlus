#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BazaarPlusPlus.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbSnapshotImagePreparer
{
    private static readonly TimeSpan PrepareTimeout = TimeSpan.FromSeconds(30);
    private static readonly int[] PngLongestEdgeTargets = [1920, 1600, 1280, 1024, 768];
    private static readonly int[] JpegQualityTargets = [90, 82, 74, 66, 58, 50];

    private readonly string _uploadCacheDirectoryPath;

    public BazaarDbSnapshotImagePreparer(string uploadCacheDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(uploadCacheDirectoryPath))
            throw new ArgumentException(
                "Upload cache directory is required.",
                nameof(uploadCacheDirectoryPath)
            );

        _uploadCacheDirectoryPath = uploadCacheDirectoryPath;
    }

    public BazaarDbSnapshotUploadImage? Prepare(
        string snapshotId,
        string absolutePath,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(snapshotId) || string.IsNullOrWhiteSpace(absolutePath))
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        var sourceBytes = File.ReadAllBytes(absolutePath);
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceBytes.Length == 0)
            return null;
        if (sourceBytes.Length <= BazaarDbSnapshotUploadLimits.MaxUploadImageBytes)
            return new BazaarDbSnapshotUploadImage
            {
                Bytes = sourceBytes,
                ContentType = "image/png",
                SourcePath = absolutePath,
            };

        var cachedPngPath = Path.Combine(_uploadCacheDirectoryPath, $"{snapshotId}.png");
        var cachedPng = TryReadCached(cachedPngPath, "image/png", cancellationToken);
        if (cachedPng != null)
            return cachedPng;

        var cachedJpegPath = Path.Combine(_uploadCacheDirectoryPath, $"{snapshotId}.jpg");
        var cachedJpeg = TryReadCached(cachedJpegPath, "image/jpeg", cancellationToken);
        if (cachedJpeg != null)
            return cachedJpeg;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PrepareTimeout);
        try
        {
            return PrepareResized(sourceBytes, cachedPngPath, cachedJpegPath, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Snapshot image preparation exceeded {PrepareTimeout.TotalSeconds:0}s."
            );
        }
    }

    private BazaarDbSnapshotUploadImage? TryReadCached(
        string path,
        string contentType,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(path))
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        var bytes = File.ReadAllBytes(path);
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length == 0 || bytes.Length > BazaarDbSnapshotUploadLimits.MaxUploadImageBytes)
            return null;

        return new BazaarDbSnapshotUploadImage
        {
            Bytes = bytes,
            ContentType = contentType,
            SourcePath = path,
        };
    }

    private static BazaarDbSnapshotUploadImage? PrepareResized(
        byte[] sourceBytes,
        string cachedPngPath,
        string cachedJpegPath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var sourceImage = Image.Load(sourceBytes);
            var sourceLongestEdge = Math.Max(sourceImage.Width, sourceImage.Height);
            foreach (var target in PngLongestEdgeTargets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (target >= sourceLongestEdge)
                    continue;

                using var candidate = ResizeToLongestEdge(sourceImage, target);
                var bytes = EncodePng(candidate);
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    bytes is { Length: > 0 }
                    && bytes.Length <= BazaarDbSnapshotUploadLimits.MaxUploadImageBytes
                )
                {
                    AtomicFileWriter.Write(cachedPngPath, bytes);
                    return new BazaarDbSnapshotUploadImage
                    {
                        Bytes = bytes,
                        ContentType = "image/png",
                        SourcePath = cachedPngPath,
                    };
                }
            }

            foreach (var target in EnumerateJpegLongestEdgeTargets(sourceLongestEdge))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var candidate = ResizeToLongestEdge(sourceImage, target);
                foreach (var quality in JpegQualityTargets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var bytes = EncodeJpeg(candidate, quality);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (
                        bytes is not { Length: > 0 }
                        || bytes.Length > BazaarDbSnapshotUploadLimits.MaxUploadImageBytes
                    )
                    {
                        continue;
                    }

                    AtomicFileWriter.Write(cachedJpegPath, bytes);
                    return new BazaarDbSnapshotUploadImage
                    {
                        Bytes = bytes,
                        ContentType = "image/jpeg",
                        SourcePath = cachedJpegPath,
                    };
                }
            }
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
        catch (InvalidImageContentException)
        {
            return null;
        }

        return null;
    }

    private static Image ResizeToLongestEdge(Image sourceImage, int longestEdge)
    {
        return sourceImage.Clone(context =>
            context.Resize(
                new ResizeOptions
                {
                    Size = new Size(longestEdge, longestEdge),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Bicubic,
                }
            )
        );
    }

    private static byte[] EncodePng(Image image)
    {
        using var stream = new MemoryStream();
        image.SaveAsPng(stream, new PngEncoder());
        return stream.ToArray();
    }

    private static byte[] EncodeJpeg(Image image, int quality)
    {
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream, new JpegEncoder { Quality = quality });
        return stream.ToArray();
    }

    private static IEnumerable<int> EnumerateJpegLongestEdgeTargets(int sourceLongestEdge)
    {
        var emittedSourceSize = false;
        if (sourceLongestEdge > 0 && sourceLongestEdge <= PngLongestEdgeTargets[0])
        {
            emittedSourceSize = true;
            yield return sourceLongestEdge;
        }

        foreach (var target in PngLongestEdgeTargets)
        {
            if (target > sourceLongestEdge)
                continue;
            if (emittedSourceSize && target == sourceLongestEdge)
                continue;

            yield return target;
        }
    }
}
