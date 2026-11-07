#nullable enable
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;
using UnityEngine;
using UnityEngine.Rendering;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

internal static class NativePortraitTexture
{
    // No synchronous ReadPixels: small GPU copies complete asynchronously while the native
    // static portrait stays visible. Pixels below use bottom-left texture coordinates.
    private static Task<bool>? _orientation;

    internal static async Task<PortraitImage> CaptureAsync(
        Texture texture,
        Rect rect,
        int maxDimension
    )
    {
        _orientation ??= CalibrateAsync();
        bool flip;
        try
        {
            flip = await _orientation;
        }
        catch
        {
            _orientation = null;
            throw;
        }
        var image = await CaptureRawAsync(texture, rect, maxDimension);
        if (flip)
            PortraitReadbackPixels.FlipRows(image);
        return image;
    }

    private static async Task<bool> CalibrateAsync()
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true)
        {
            name = "BPP_PortraitReadbackCalibration",
            filterMode = FilterMode.Point,
        };
        try
        {
            texture.SetPixels32(
                new Color32[]
                {
                    new(255, 0, 0, 255),
                    new(255, 0, 0, 255),
                    new(0, 0, 255, 255),
                    new(0, 0, 255, 255),
                }
            );
            texture.Apply(false, true);
            var image = await CaptureRawAsync(texture, new Rect(0, 0, 2, 2), 2);
            return PortraitReadbackPixels.NeedsRowFlip(image);
        }
        finally
        {
            UnityEngine.Object.Destroy(texture);
        }
    }

    private static Task<PortraitImage> CaptureRawAsync(Texture texture, Rect rect, int maxDimension)
    {
        if (!SystemInfo.supportsAsyncGPUReadback)
            throw new NotSupportedException(
                "Automatic portrait composition needs asynchronous GPU readback."
            );
        var factor = Math.Min(1f, maxDimension / Math.Max(rect.width, rect.height));
        var width = Math.Max(2, (int)MathF.Round(rect.width * factor));
        var height = Math.Max(2, (int)MathF.Round(rect.height * factor));
        var rt = new RenderTexture(
            width,
            height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB
        )
        {
            name = "BPP_PortraitCompositionReadback",
            useMipMap = false,
            autoGenerateMips = false,
        };
        var completion = new TaskCompletionSource<PortraitImage>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        try
        {
            if (!rt.Create())
                throw new InvalidOperationException("Cannot create the portrait readback texture.");
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(
                    texture,
                    rt,
                    new Vector2(rect.width / texture.width, rect.height / texture.height),
                    new Vector2(rect.x / texture.width, rect.y / texture.height)
                );
            }
            finally
            {
                RenderTexture.active = previous;
            }
            AsyncGPUReadback.Request(
                rt,
                0,
                TextureFormat.RGBA32,
                request =>
                {
                    try
                    {
                        if (request.hasError)
                            throw new InvalidOperationException(
                                "Portrait texture readback failed."
                            );
                        var bytes = new byte[width * height * 4];
                        request.GetData<byte>().CopyTo(bytes);
                        completion.TrySetResult(new PortraitImage(width, height, bytes));
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(ex);
                    }
                    finally
                    {
                        rt.Release();
                        UnityEngine.Object.Destroy(rt);
                    }
                }
            );
        }
        catch
        {
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            throw;
        }
        return completion.Task;
    }
}
