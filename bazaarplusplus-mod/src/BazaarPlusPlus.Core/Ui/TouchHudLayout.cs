#nullable enable
namespace BazaarPlusPlus.Core.Ui;

/// <summary>Touch targets around native HUD anchors; visible artwork may be smaller.</summary>
public static class TouchHudLayout
{
    /// <summary>
    /// The target left of the settings button, level with it. A target is wider than the gear, so
    /// it ends left of <see cref="ShortcutBelow"/>'s as well, and the corner's hit areas never meet.
    /// </summary>
    public static TouchRect ShortcutBeside(TouchMetrics metrics, TouchRect gear)
    {
        var target = metrics.MinTarget;
        var safe = metrics.SafeArea;
        var top = Math.Clamp(gear.CenterY - target / 2, safe.Top, safe.Bottom - target);
        var right = Math.Min(gear.Left, ShortcutBelow(metrics, gear).Left) - metrics.Dp(4);
        return new TouchRect(Math.Max(safe.Left, right - target), top, target, target);
    }

    /// <summary>The target below the settings button, centred under it.</summary>
    public static TouchRect ShortcutBelow(TouchMetrics metrics, TouchRect gear)
    {
        var target = metrics.MinTarget;
        var safe = metrics.SafeArea;
        var left = Math.Clamp(gear.CenterX - target / 2, safe.Left, safe.Right - target);
        var top = Math.Max(safe.Top, gear.Bottom + metrics.Dp(4));
        return new TouchRect(left, top, target, target);
    }

    /// <summary>
    /// Whether the clock can sit beside <paramref name="vault"/>, the box read from the board:
    /// wholly on the canvas, right of center, and at least a touch target tall.
    /// </summary>
    public static bool FitsBesideVault(TouchMetrics metrics, TouchRect vault) =>
        float.IsFinite(vault.Left)
        && float.IsFinite(vault.Top)
        && float.IsFinite(vault.Width)
        && float.IsFinite(vault.Height)
        && new TouchRect(0, 0, metrics.CanvasWidth, metrics.CanvasHeight).Contains(vault)
        && vault.CenterX > metrics.CanvasWidth / 2
        && vault.Height >= metrics.MinTarget;

    /// <summary>
    /// The combat clock right of <paramref name="vault"/>: the plaque spans the vault's height,
    /// the time row sits above a shorter control row, and the speed and pause hit areas keep a
    /// full <see cref="TouchMetrics.MinTarget"/> by extending below the plaque towards the
    /// screen edge.
    /// </summary>
    public static PlaybackLayout Playback(TouchMetrics metrics, TouchRect vault)
    {
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
