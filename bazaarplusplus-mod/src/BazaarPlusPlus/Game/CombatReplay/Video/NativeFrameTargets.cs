#nullable enable
using UnityEngine;
using UnityEngine.Rendering;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class MetalFrameTarget(MacMetalVideoEncoder encoder, CommandBuffer commands)
    : INativeFrameTarget
{
    public void Clear() => commands.Clear();

    public NativeFramePreparation Prepare(
        long firstFrameIndex,
        int frameCount,
        out IntPtr eventData
    )
    {
        eventData = IntPtr.Zero;
        if (!encoder.TryAcquireFrame(out var lease))
            return NativeFramePreparation.LeaseUnavailable;
        if (encoder.TryPrepareRenderEvent(lease, firstFrameIndex, frameCount, out eventData))
            return NativeFramePreparation.Ready;
        encoder.ReleaseFrame(lease);
        return NativeFramePreparation.Rejected;
    }

    public void Queue(IntPtr eventData) =>
        commands.IssuePluginEventAndData(encoder.RenderEventFunction, eventID: 1, eventData);

    public void ExecuteAndCommit(IntPtr eventData)
    {
        Graphics.ExecuteCommandBuffer(commands);
        encoder.CommitRenderEvent(eventData);
    }

    public void Cancel(IntPtr eventData) => encoder.CancelRenderEvent(eventData);

    public void Discard(IntPtr eventData) => encoder.DiscardRenderEvent(eventData);
}

internal sealed class WindowsFrameTarget(
    WindowsMediaFoundationVideoEncoder encoder,
    CommandBuffer commands,
    RenderTexture texture
) : INativeFrameTarget
{
    public void Clear() => commands.Clear();

    public NativeFramePreparation Prepare(
        long firstFrameIndex,
        int frameCount,
        out IntPtr eventData
    )
    {
        eventData = IntPtr.Zero;
        if (!encoder.TryAcquireFrame(out var lease))
            return NativeFramePreparation.LeaseUnavailable;
        if (encoder.TryPrepareRenderEvent(lease, firstFrameIndex, frameCount, out eventData))
            return NativeFramePreparation.Ready;
        encoder.ReleaseFrame(lease);
        return NativeFramePreparation.Rejected;
    }

    public void Queue(IntPtr eventData)
    {
        ScreenCapture.CaptureScreenshotIntoRenderTexture(texture);
        commands.IssuePluginEventAndData(encoder.RenderEventFunction, eventID: 1, eventData);
    }

    public void ExecuteAndCommit(IntPtr eventData)
    {
        Graphics.ExecuteCommandBuffer(commands);
        encoder.CommitRenderEvent(eventData);
    }

    public void Cancel(IntPtr eventData) => encoder.CancelRenderEvent(eventData);

    public void Discard(IntPtr eventData) => encoder.DiscardRenderEvent(eventData);
}
