using System.Runtime.InteropServices;
using UnityEngine;

namespace BazaarPlusPlus.Android.GameInterop;

// Capture after WaitForEndOfFrame because the back buffer may still be drawing before then.
internal static class AndroidScreenCapture
{
    public readonly record struct Frame(byte[] Rgba, int Width, int Height);

    public static Frame ReadScreen()
    {
        var width = Screen.width;
        var height = Screen.height;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException(
                $"Cannot capture a screenshot of size {width}x{height}."
            );

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "BPP_EndOfRunScreenshot",
        };
        try
        {
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            var raw = texture.GetRawTextureData();
            var length = width * height * 4;
            if (raw == null || raw.Length < length)
                throw new InvalidOperationException(
                    $"Texture data holds {raw?.Length ?? 0} bytes, expected {length}."
                );
            var pixels = new byte[length];
            // The IL2CPP array data starts four pointer widths past its base. Copy it at once to
            // avoid an interop call per byte.
            Marshal.Copy(IntPtr.Add(raw.Pointer, 4 * IntPtr.Size), pixels, 0, length);
            return new Frame(pixels, width, height);
        }
        finally
        {
            UnityEngine.Object.Destroy(texture);
        }
    }
}
