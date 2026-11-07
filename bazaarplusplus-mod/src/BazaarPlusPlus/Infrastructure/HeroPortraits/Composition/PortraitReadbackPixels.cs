#nullable enable

namespace BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

internal static class PortraitReadbackPixels
{
    // The calibration texture has an opaque red lower row and blue upper row. Probe the
    // same texture-to-RT-to-readback path rather than inferring it from framebuffer flags.
    internal static bool NeedsRowFlip(PortraitImage calibration)
    {
        if (calibration.Width != 2 || calibration.Height != 2 || calibration.Rgba.Length != 16)
            throw new InvalidOperationException("Invalid portrait readback calibration size.");
        var bytes = calibration.Rgba;
        var firstRed = bytes[0] > 240 && bytes[2] < 15;
        var firstBlue = bytes[2] > 240 && bytes[0] < 15;
        var lastRed = bytes[8] > 240 && bytes[10] < 15;
        var lastBlue = bytes[10] > 240 && bytes[8] < 15;
        if (firstRed && lastBlue)
            return false;
        if (firstBlue && lastRed)
            return true;
        throw new InvalidOperationException(
            "Cannot determine portrait texture readback orientation."
        );
    }

    internal static void FlipRows(PortraitImage image)
    {
        var stride = image.Width * 4;
        var row = new byte[stride];
        for (var y = 0; y < image.Height / 2; y++)
        {
            var a = y * stride;
            var b = (image.Height - 1 - y) * stride;
            Buffer.BlockCopy(image.Rgba, a, row, 0, stride);
            Buffer.BlockCopy(image.Rgba, b, image.Rgba, a, stride);
            Buffer.BlockCopy(row, 0, image.Rgba, b, stride);
        }
    }
}
