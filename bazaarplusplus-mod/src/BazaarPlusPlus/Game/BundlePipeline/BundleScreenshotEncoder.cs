#nullable enable
using BazaarPlusPlus.ModApi.Bundle;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace BazaarPlusPlus.Game.BundlePipeline;

internal sealed class BundleScreenshotEncoder
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly int[] LongestEdges = [1920, 1600, 1280, 1024, 768];
    private static readonly int[] Qualities = [90, 82, 74, 66, 58, 50];

    internal async Task<BundleScreenshotBuildInputV5?> EncodeAsync(
        string absolutePath,
        long capturedAtMs,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var task = Task.Run(() => Encode(absolutePath, capturedAtMs, timeout.Token), timeout.Token);
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static BundleScreenshotBuildInputV5? Encode(
        string absolutePath,
        long capturedAtMs,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var source = Image.Load(absolutePath);
        var sourceEdge = Math.Max(source.Width, source.Height);
        foreach (var edge in EnumerateEdges(sourceEdge))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var image = Resize(source, edge);
            foreach (var quality in Qualities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var output = new MemoryStream();
                image.SaveAsJpeg(output, new JpegEncoder { Quality = quality });
                if (output.Length is <= 0 or > BundleLimitsV5.MaxScreenshotBytes)
                    continue;
                return new BundleScreenshotBuildInputV5
                {
                    Bytes = output.ToArray(),
                    Width = image.Width,
                    Height = image.Height,
                    Quality = quality,
                    CapturedAtMs = capturedAtMs,
                };
            }
        }
        return null;
    }

    private static IEnumerable<int> EnumerateEdges(int sourceEdge)
    {
        if (sourceEdge <= LongestEdges[0])
            yield return sourceEdge;
        foreach (var edge in LongestEdges)
        {
            if (edge < sourceEdge)
                yield return edge;
        }
    }

    private static Image Resize(Image source, int edge) =>
        source.Clone(context =>
            context.Resize(
                new ResizeOptions
                {
                    Size = new Size(edge, edge),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Bicubic,
                }
            )
        );
}
