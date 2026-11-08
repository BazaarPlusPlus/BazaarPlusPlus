using System.Runtime.InteropServices;
using UnityEngine;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// Reads the frame the screen shows into managed RGBA bytes, top row first. Main thread only,
/// and only after <c>WaitForEndOfFrame</c>: before that the back buffer holds a frame still
/// being drawn. The loader runs a managed coroutine through the game's own
/// <c>StartCoroutine</c>, so a yielded <see cref="WaitForEndOfFrame"/> reaches Unity's scheduler.
/// </summary>
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
            // One copy out of IL2CPP memory; the interop indexer would cost a call per byte. The
            // elements follow the array header (object header, bounds, length): four pointers,
            // which is what the interop's own non-public ArrayStartPointer adds.
            Marshal.Copy(IntPtr.Add(raw.Pointer, 4 * IntPtr.Size), pixels, 0, length);
            return new Frame(pixels, width, height);
        }
        finally
        {
            UnityEngine.Object.Destroy(texture);
        }
    }
}
