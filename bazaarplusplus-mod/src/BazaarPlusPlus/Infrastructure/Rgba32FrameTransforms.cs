#nullable enable
using System;

namespace BazaarPlusPlus.Infrastructure;

internal static class Rgba32FrameTransforms
{
    public static void FlipVerticalRgba32(byte[] buffer, int width, int height)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));
        if (width <= 0 || height <= 0)
            return;

        var stride = width * 4;
        var expectedLength = stride * height;
        if (buffer.Length < expectedLength)
            return;

        var rowBuffer = new byte[stride];
        for (var row = 0; row < height / 2; row++)
        {
            var topOffset = row * stride;
            var bottomOffset = (height - 1 - row) * stride;

            Buffer.BlockCopy(buffer, topOffset, rowBuffer, 0, stride);
            Buffer.BlockCopy(buffer, bottomOffset, buffer, topOffset, stride);
            Buffer.BlockCopy(rowBuffer, 0, buffer, bottomOffset, stride);
        }
    }
}
