#nullable enable
using BazaarPlusPlus.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UnityEngine;
using UnityEngine.Rendering;

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

    public ScreenshotCaptureSession BeginCaptureCurrentFrame(ScreenshotCaptureRequest request)
    {
        if (string.IsNullOrWhiteSpace(_directoryPath))
        {
            return new ScreenshotCaptureSession(
                Task.CompletedTask,
                Task.FromResult<ScreenshotCaptureResult?>(null)
            );
        }
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.ScreenshotId))
            throw new ArgumentException("Screenshot id is required.", nameof(request));

        var capturedAtLocal = _nowProvider();
        var capturedAtUtc = capturedAtLocal.ToUniversalTime();
        var relativePath = ScreenshotPathBuilder.BuildRelativePath(request.RunId, capturedAtLocal);
        var filePath = Path.Combine(_directoryPath, relativePath);

        var result = new ScreenshotCaptureResult
        {
            ScreenshotId = request.ScreenshotId,
            RunId = request.RunId,
            HeroName = request.HeroName,
            BattleId = request.BattleId,
            CaptureSource = request.CaptureSource,
            RelativePath = relativePath,
            FilePath = filePath,
            CapturedAtLocal = capturedAtLocal,
            CapturedAtUtc = capturedAtUtc,
        };
        var frameAcquired = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var captureAndWrite = CaptureAndWriteCurrentFrameAsync(
            filePath,
            request.ScreenshotId,
            frameAcquired
        );

        var completion = captureAndWrite.ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                    throw task.Exception!.GetBaseException();
                if (task.IsCanceled || !task.Result)
                    return null;

                return result;
            },
            TaskScheduler.Default
        );
        return new ScreenshotCaptureSession(frameAcquired.Task, completion);
    }

    private static Task<bool> CaptureAndWriteCurrentFrameAsync(
        string filePath,
        string screenshotId,
        TaskCompletionSource<bool> frameAcquired
    )
    {
        var width = Screen.width;
        var height = Screen.height;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException(
                $"Cannot capture screenshot with invalid size {width}x{height}."
            );

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        RenderTexture? renderTexture = null;
        try
        {
            renderTexture = new RenderTexture(
                width,
                height,
                depth: 0,
                format: RenderTextureFormat.ARGB32
            )
            {
                name = "BPP_EndOfRunScreenshot",
                useMipMap = false,
                autoGenerateMips = false,
            };
            if (!renderTexture.Create())
                throw new InvalidOperationException(
                    $"Failed to create RenderTexture {width}x{height} for screenshot capture."
                );

            ScreenCapture.CaptureScreenshotIntoRenderTexture(renderTexture);
            var capturedTexture = renderTexture;
            AsyncGPUReadback.Request(
                capturedTexture,
                0,
                TextureFormat.RGBA32,
                request =>
                    OnReadbackComplete(
                        request,
                        capturedTexture,
                        width,
                        height,
                        filePath,
                        screenshotId,
                        frameAcquired,
                        completion
                    )
            );
        }
        catch
        {
            ReleaseRenderTexture(renderTexture, screenshotId, filePath);
            throw;
        }

        return completion.Task;
    }

    private static void OnReadbackComplete(
        AsyncGPUReadbackRequest request,
        RenderTexture renderTexture,
        int width,
        int height,
        string filePath,
        string screenshotId,
        TaskCompletionSource<bool> frameAcquired,
        TaskCompletionSource<bool> completion
    )
    {
        try
        {
            if (request.hasError)
            {
                var failure = new InvalidOperationException("AsyncGPUReadback returned an error.");
                frameAcquired.TrySetException(failure);
                completion.TrySetException(failure);
                return;
            }

            var pixels = new byte[width * height * 4];
            request.GetData<byte>().CopyTo(pixels);
            if (!SystemInfo.graphicsUVStartsAtTop)
                Rgba32FrameTransforms.FlipVerticalRgba32(pixels, width, height);

            // The settled frame is now detached from Unity. Navigation cannot change these
            // pixels, so the caller may release input while PNG encoding continues off-thread.
            frameAcquired.TrySetResult(true);
            _ = Task.Run(() =>
            {
                try
                {
                    WriteRgba32PngAtomically(filePath, pixels, width, height);
                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
        }
        catch (Exception ex)
        {
            frameAcquired.TrySetException(ex);
            completion.TrySetException(ex);
        }
        finally
        {
            ReleaseRenderTexture(renderTexture, screenshotId, filePath);
        }
    }

    private static void WriteRgba32PngAtomically(
        string filePath,
        byte[] pixels,
        int width,
        int height
    )
    {
        AtomicFileWriter.Write(
            filePath,
            tempPath =>
            {
                using var image = Image.LoadPixelData<Rgba32>(pixels, width, height);
                image.SaveAsPng(tempPath);
            }
        );
    }

    private static void ReleaseRenderTexture(
        RenderTexture? renderTexture,
        string screenshotId,
        string filePath
    )
    {
        if (renderTexture == null)
            return;

        try
        {
            if (renderTexture.IsCreated())
                renderTexture.Release();
            UnityEngine.Object.Destroy(renderTexture);
        }
        catch (Exception ex)
        {
            ScreenshotCaptureDiagnostics.ReportCleanupFailed(
                ScreenshotCaptureCleanupStage.RenderTextureRelease,
                screenshotId,
                filePath,
                ex
            );
        }
    }
}
