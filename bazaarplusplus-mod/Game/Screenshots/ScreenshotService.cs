#nullable enable
using System;
using System.IO;
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class ScreenshotService
{
    private readonly string _directoryPath;
    private readonly Func<DateTimeOffset> _nowProvider;

    public ScreenshotService(string directoryPath, Func<DateTimeOffset>? nowProvider = null)
    {
        _directoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
        _nowProvider = nowProvider ?? (() => DateTimeOffset.Now);
    }

    public ScreenshotCaptureResult? CaptureCurrentFrame(ScreenshotCaptureRequest request)
    {
        if (string.IsNullOrWhiteSpace(_directoryPath))
            return null;
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            var capturedAtLocal = _nowProvider();
            var capturedAtUtc = capturedAtLocal.ToUniversalTime();
            var screenshotId = Guid.NewGuid().ToString("N");
            var relativePath = ScreenshotPathBuilder.BuildRelativePath(
                request.RunId,
                capturedAtLocal
            );
            var filePath = Path.Combine(_directoryPath, relativePath);
            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
                Directory.CreateDirectory(directoryPath);

            WriteCurrentFrameToFile(filePath);
            BppLog.Info("ScreenshotService", $"Saved screenshot: {filePath}");
            return new ScreenshotCaptureResult
            {
                ScreenshotId = screenshotId,
                RunId = request.RunId,
                HeroName = request.HeroName,
                BattleId = request.BattleId,
                CaptureSource = request.CaptureSource,
                RelativePath = relativePath,
                FilePath = filePath,
                CapturedAtLocal = capturedAtLocal,
                CapturedAtUtc = capturedAtUtc,
            };
        }
        catch (Exception ex)
        {
            BppLog.Error("ScreenshotService", "Failed to capture screenshot.", ex);
            return null;
        }
    }

    private static void WriteCurrentFrameToFile(string filePath)
    {
        var width = Screen.width;
        var height = Screen.height;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException(
                $"Cannot capture screenshot with invalid size {width}x{height}."
            );

        Texture2D? texture = null;
        var previousActive = RenderTexture.active;
        try
        {
            texture = new Texture2D(width, height, TextureFormat.RGB24, mipChain: false);
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, recalculateMipMaps: false);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            var pngBytes = texture.EncodeToPNG();
            if (pngBytes == null || pngBytes.Length == 0)
                throw new InvalidOperationException("Screenshot PNG encoding returned no data.");

            File.WriteAllBytes(filePath, pngBytes);
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (texture != null)
                UnityEngine.Object.Destroy(texture);
        }
    }
}
