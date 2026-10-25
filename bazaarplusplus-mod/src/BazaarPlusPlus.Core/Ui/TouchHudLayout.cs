#nullable enable
namespace BazaarPlusPlus.Core.Ui;

/// <summary>Touch targets around native HUD anchors; visible artwork may be smaller.</summary>
public static class TouchHudLayout
{
    public static TouchRect[] Shortcuts(TouchMetrics metrics, TouchRect gear, int count)
    {
        var target = metrics.MinTarget;
        var safe = metrics.SafeArea;
        var gap = metrics.Dp(4);
        var left = Math.Clamp(gear.CenterX - target / 2, safe.Left, safe.Right - target);
        var top = Math.Max(safe.Top, gear.Bottom + gap);
        return Enumerable
            .Range(0, count)
            .Select(index => new TouchRect(left, top + index * (target + gap), target, target))
            .ToArray();
    }

    /// <summary>
    /// The vault's frame. Native board art is height-scaled and centered: the frame's right
    /// border is 0.59 canvas heights right of center, and it spans 0.761 to 0.978 canvas heights
    /// from the top, level with the backpack opposite it.
    /// </summary>
    public static TouchRect Vault(TouchMetrics metrics)
    {
        var height = metrics.CanvasHeight;
        return new(
            metrics.CanvasWidth / 2 + height * .378f,
            height * .761f,
            height * .212f,
            height * .217f
        );
    }

    /// <summary>
    /// The combat clock right of the vault: the plaque spans the vault frame's height, the time
    /// row sits above a shorter control row, and the speed and pause hit areas keep a full
    /// <see cref="TouchMetrics.MinTarget"/> by extending below the plaque towards the screen edge.
    /// </summary>
    public static PlaybackLayout Playback(TouchMetrics metrics)
    {
        var vault = Vault(metrics);
        var target = metrics.MinTarget;
        var padding = metrics.Dp(4);
        var safe = metrics.SafeArea;
        var width = 2 * target + 2 * padding;
        var left = Math.Clamp(vault.Right + padding, safe.Left, safe.Right - width);
        var bottom = Math.Min(vault.Bottom, safe.Bottom);
        var top = Math.Max(safe.Top, Math.Min(vault.Top, bottom - metrics.Dp(80)));
        var inner = new TouchRect(left, top, width, bottom - top).Inset(padding);
        var controlsHeight = inner.Height * .42f;
        var time = inner with { Height = inner.Height - controlsHeight };
        var controls = inner with { Top = time.Bottom, Height = controlsHeight };
        var hitTop = Math.Min(controls.Top, safe.Bottom - target);
        var hitHeight = safe.Bottom - hitTop;
        return new(
            new(left, top, width, bottom - top),
            time,
            controls,
            new(inner.Left, hitTop, target, hitHeight),
            new(inner.Left + target, hitTop, target, hitHeight)
        );
    }
}

/// <summary>
/// <see cref="Time"/> and <see cref="Controls"/> are the visible rows; <see cref="Speed"/> and
/// <see cref="Pause"/> are the hit areas under the control row's left and right halves.
/// </summary>
public readonly record struct PlaybackLayout(
    TouchRect Panel,
    TouchRect Time,
    TouchRect Controls,
    TouchRect Speed,
    TouchRect Pause
);
