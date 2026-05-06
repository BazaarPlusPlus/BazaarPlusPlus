#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.MonsterPreview;

namespace BazaarPlusPlus.Game.PreviewSurface;

internal sealed class PreviewBoardRenderTarget : IBoardRenderTarget, IDisposable
{
    private const string LogArea = "PreviewBoardRenderTarget";
    private readonly IPreviewBoardSurface _surface;
    private readonly object _sync = new();
    private Task _surfaceWork = Task.CompletedTask;
    private CancellationTokenSource? _renderCts;
    private bool _disposed;

    internal PreviewBoardRenderTarget(IPreviewBoardSurface surface)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    public bool IsAlive => !_disposed && _surface.IsAlive;

    public void Dispose()
    {
        Task pendingWork;
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            CancelActiveRenderUnsafe();
            pendingWork = _surfaceWork;
            _surfaceWork = Task.CompletedTask;
        }

        try
        {
            pendingWork.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            TryLogException("Dispose observed a render task failure", ex);
        }

        _surface.Dispose();
    }

    public void Render(BoardRenderModel renderModel)
    {
        ObserveBackgroundTask(QueueRenderAsync(renderModel), "Render");
    }

    internal Task QueueRenderAsync(BoardRenderModel renderModel)
    {
        renderModel ??= new BoardRenderModel();
        lock (_sync)
        {
            if (_disposed || !_surface.IsAlive)
            {
                LogWarn(
                    $"QueueRenderAsync ignored disposed={_disposed} surfaceAlive={_surface.IsAlive}"
                );
                return Task.CompletedTask;
            }

            CancelActiveRenderUnsafe();
            var cts = new CancellationTokenSource();
            _renderCts = cts;
            var queuedModel = renderModel;
            LogInfo(
                $"QueueRenderAsync signature={queuedModel.Data?.Signature ?? string.Empty} visible={queuedModel.Presentation?.Visible ?? false} items={queuedModel.Data?.ItemCards?.Count ?? 0} skills={queuedModel.Data?.SkillCards?.Count ?? 0}"
            );
            _surfaceWork = EnqueueSurfaceWorkAsync(() =>
                RenderSurfaceAsync(queuedModel, cts.Token)
            );
            return _surfaceWork;
        }
    }

    public void SetVisible(bool visible)
    {
        ObserveBackgroundTask(QueueSetVisibleAsync(visible), "SetVisible");
    }

    internal Task QueueSetVisibleAsync(bool visible)
    {
        lock (_sync)
        {
            if (_disposed || !_surface.IsAlive)
            {
                LogWarn(
                    $"QueueSetVisibleAsync ignored disposed={_disposed} surfaceAlive={_surface.IsAlive} visible={visible}"
                );
                return Task.CompletedTask;
            }

            if (!visible)
                CancelActiveRenderUnsafe();

            LogDebug($"QueueSetVisibleAsync visible={visible}");
            _surfaceWork = EnqueueSurfaceWorkAsync(() => ApplyVisibilityAsync(visible));
            return _surfaceWork;
        }
    }

    private async Task RenderSurfaceAsync(
        BoardRenderModel renderModel,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (_disposed || !_surface.IsAlive || cancellationToken.IsCancellationRequested)
                return;

            var presentation = renderModel.Presentation ?? new PreviewBoardPresentation();
            var pose = renderModel.Pose ?? new BoardPose();
            _surface.SetPresentation(presentation);
            _surface.SetDebugOptions(renderModel.Debug ?? new PreviewBoardDebugOptions());
            _surface.UpdateAnchor(pose.Position, pose.Rotation);
            _surface.SetVisible(presentation.Visible);

            if (cancellationToken.IsCancellationRequested)
            {
                LogWarn(
                    $"RenderSurfaceAsync cancelled before surface render signature={renderModel.Data?.Signature ?? string.Empty}"
                );
                return;
            }

            LogInfo(
                $"RenderSurfaceAsync start signature={renderModel.Data?.Signature ?? string.Empty} pose={pose.Position}"
            );
            await _surface.RenderAsync(
                renderModel.Data ?? new PreviewBoardModel(),
                cancellationToken
            );
            LogInfo(
                $"RenderSurfaceAsync completed signature={renderModel.Data?.Signature ?? string.Empty}"
            );
        }
        catch (OperationCanceledException)
        {
            LogWarn(
                $"RenderSurfaceAsync observed OperationCanceledException signature={renderModel.Data?.Signature ?? string.Empty}"
            );
        }
    }

    private Task ApplyVisibilityAsync(bool visible)
    {
        if (_disposed || !_surface.IsAlive)
            return Task.CompletedTask;

        if (!visible)
        {
            _surface.Clear();
            LogInfo("ApplyVisibilityAsync cleared surface because visible=false");
        }

        _surface.SetVisible(visible);
        LogDebug($"ApplyVisibilityAsync visible={visible}");
        return Task.CompletedTask;
    }

    private Task EnqueueSurfaceWorkAsync(Func<Task> work)
    {
        var previousWork = _surfaceWork;
        return ContinueAfterAsync(previousWork, work);
    }

    private static async Task ContinueAfterAsync(Task previousWork, Func<Task> nextWork)
    {
        try
        {
            await previousWork.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            TryLogException(
                "Previous preview surface task failed; continuing with the latest request",
                ex
            );
        }

        try
        {
            await nextWork().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TryLogException("Preview surface task failed", ex);
        }
    }

    private void CancelActiveRenderUnsafe()
    {
        if (_renderCts == null)
            return;

        LogDebug("CancelActiveRenderUnsafe cancelling current render");
        _renderCts.Cancel();
        _renderCts.Dispose();
        _renderCts = null;
    }

    private static void LogInfo(string message)
    {
        TryLog("Info", message);
    }

    private static void LogWarn(string message)
    {
        TryLog("Warn", message);
    }

    private static void LogDebug(string message)
    {
        TryLog("Debug", message);
    }

    private static void TryLog(string methodName, string message)
    {
        var bppLogType = Type.GetType("BazaarPlusPlus.BppLog, BazaarPlusPlus");
        var method = bppLogType?.GetMethod(
            methodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
        );
        method?.Invoke(null, new object[] { LogArea, message });
    }

    private static void TryLogException(string message, Exception ex)
    {
        var bppLogType = Type.GetType("BazaarPlusPlus.BppLog, BazaarPlusPlus");
        var method = bppLogType?.GetMethod(
            "Error",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null,
            [typeof(string), typeof(string), typeof(Exception)],
            null
        );
        method?.Invoke(null, new object[] { LogArea, message, ex });
    }

    private static void ObserveBackgroundTask(Task task, string operationName)
    {
        if (task.IsCompletedSuccessfully || task.IsCanceled)
            return;

        _ = task.ContinueWith(
            continuation =>
            {
                if (continuation.IsFaulted && continuation.Exception != null)
                {
                    var exception = continuation.Exception.GetBaseException();
                    if (exception is not OperationCanceledException)
                        TryLogException($"{operationName} task failed", exception);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }
}
