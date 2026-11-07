using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;
using Xunit;

namespace PureBehavior.Tests;

public sealed class PortraitCompositionTests
{
    [Fact]
    public void Matching_sixteen_poses_preserves_the_fit_without_large_temporary_allocations()
    {
        var image = Image(128, 128);
        PortraitTriangle[] triangles =
        [
            new(0, new(0, 0), new(1, 0), new(1, 1), new(0, 0), new(1, 0), new(1, 1)),
            new(0, new(1, 1), new(0, 1), new(0, 0), new(1, 1), new(0, 1), new(0, 0)),
        ];
        var input = new PortraitCompositionInput(
            image,
            [image],
            Enumerable.Range(0, 16).Select(i => new PortraitPose(i, triangles)).ToArray()
        );
        PortraitCompositionMatcher.Match(input, CancellationToken.None);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var fit = PortraitCompositionMatcher.Match(input, CancellationToken.None);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        Assert.NotNull(fit);
        Assert.InRange(fit.Scale, 0.99999f, 1.00001f);
        Assert.InRange(MathF.Abs(fit.X) + MathF.Abs(fit.Y), 0f, 0.00001f);
        Assert.True(fit.Inliers >= 14);
        Assert.True(allocated < 16 * 1024 * 1024, $"Allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Feature_extraction_reuses_image_scratch_storage()
    {
        var image = Image(512, 512);
        PortraitImageFeatures.Extract(image, CancellationToken.None);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var features = PortraitImageFeatures.Extract(image, CancellationToken.None);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        Assert.NotEmpty(features);
        Assert.True(allocated < 8 * 1024 * 1024, $"Allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Different_sizes_and_transparent_padding_do_not_change_later_descriptors()
    {
        var image = Image(127, 93);
        for (var i = 0; i < image.Rgba.Length; i += 4)
            if (i % 11 == 0)
                image.Rgba[i + 3] = 0;
        var expected = PortraitImageFeatures.Extract(image, CancellationToken.None);
        PortraitImageFeatures.Extract(Image(256, 256), CancellationToken.None);
        Assert.Empty(
            PortraitImageFeatures.Extract(
                new PortraitImage(127, 93, new byte[127 * 93 * 4]),
                CancellationToken.None
            )
        );
        var actual = PortraitImageFeatures.Extract(image, CancellationToken.None);

        Assert.NotEmpty(expected);
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].X, actual[i].X);
            Assert.Equal(expected[i].Y, actual[i].Y);
            Assert.Equal(expected[i].Descriptor, actual[i].Descriptor);
        }
    }

    [Fact]
    public void Cancelled_extraction_leaves_the_next_match_usable()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var image = Image(128, 128);
        Assert.Throws<OperationCanceledException>(() =>
            PortraitImageFeatures.Extract(image, cancellation.Token)
        );
        Assert.NotEmpty(PortraitImageFeatures.Extract(image, CancellationToken.None));
    }

    private static PortraitImage Image(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var random = new Random(81);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            var noise = random.Next(32);
            pixels[i] = (byte)((x * 7 + y * 3 + noise) % 256);
            pixels[i + 1] = (byte)((x * 3 + y * 11 + noise) % 256);
            pixels[i + 2] = (byte)((x * 13 + y * 5 + noise) % 256);
            pixels[i + 3] = 255;
        }
        return new PortraitImage(width, height, pixels);
    }
}
