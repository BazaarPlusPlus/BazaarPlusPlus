#nullable enable
using BazaarPlusPlus.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BazaarPlusPlus.Game.Screenshots;

/// <summary>Writes a captured frame's RGBA pixels as the end-of-run PNG every host saves.</summary>
public static class ScreenshotPngWriter
{
    // GPU readback rows arrive bottom-up when UVs start at the bottom; PNG rows are top-down.
    public static void FlipRowsVertically(byte[] pixels, int stride, int height)
    {
        if (pixels == null)
            throw new ArgumentNullException(nameof(pixels));
        var rowBuffer = new byte[stride];
        for (var row = 0; row < height / 2; row++)
        {
            var topOffset = row * stride;
            var bottomOffset = (height - 1 - row) * stride;
            Buffer.BlockCopy(pixels, topOffset, rowBuffer, 0, stride);
            Buffer.BlockCopy(pixels, bottomOffset, pixels, topOffset, stride);
            Buffer.BlockCopy(rowBuffer, 0, pixels, bottomOffset, stride);
        }
    }

    // The readback carries the framebuffer's alpha, which the screen ignores: UI blending
    // leaves it below opaque over cards, so viewers would show those pixels translucent.
    // Keep only the colours the player saw.
    public static void WriteOpaquePngAtomically(
        string filePath,
        byte[] rgbaPixels,
        int width,
        int height
    )
    {
        AtomicFileWriter.Write(
            filePath,
            tempPath =>
            {
                using var image = Image.LoadPixelData<Rgba32>(rgbaPixels, width, height);
                using var opaque = image.CloneAs<Rgb24>();
                opaque.SaveAsPng(tempPath);
            }
        );
    }
}
