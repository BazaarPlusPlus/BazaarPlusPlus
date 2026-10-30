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

    /// <summary>
    /// The <paramref name="index"/>th target left of the settings button (from 0), level with it:
    /// a row along the screen's top edge for a board that reaches up to just under the gear.
    /// </summary>
    public static TouchRect ShortcutInRow(TouchMetrics metrics, TouchRect gear, int index)
    {
        var target = metrics.MinTarget;
        var safe = metrics.SafeArea;
        var gap = metrics.Dp(4);
        var top = Math.Clamp(gear.CenterY - target / 2, safe.Top, safe.Bottom - target);
        var right = gear.Left - gap - index * (target + gap);
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
    /// The combat clock beside <paramref name="vault"/>. Where the screen leaves a band under the
    /// board for a <see cref="TouchMetrics.MinTarget"/> row, as a screen squarer than 16:10 does,
    /// the clock is one row in it, centred under the vault: the time, then speed and pause.
    /// Otherwise it sits right of the vault and spans its height, the time row above a shorter
    /// control row. Either way the speed and pause hit areas keep a full
    /// <see cref="TouchMetrics.MinTarget"/> by extending below the plaque towards the screen edge.
    /// </summary>
    public static PlaybackLayout Playback(TouchMetrics metrics, TouchRect vault)
    {
        var target = metrics.MinTarget;
        var padding = metrics.Dp(4);
        var safe = metrics.SafeArea;
        TouchRect time;
        TouchRect controls;
        TouchRect panel;
        var band = safe.Bottom - vault.Bottom - padding;
        if (band >= target)
        {
            var height = Math.Min(band, metrics.Dp(RowHeightDp));
            var width = metrics.Dp(RowTimeWidthDp) + 2 * target + 2 * padding;
            var left = Math.Clamp(vault.CenterX - width / 2, safe.Left, safe.Right - width);
            panel = new(left, vault.Bottom + padding, width, height);
            var inner = panel.Inset(padding);
            controls = inner with { Left = inner.Right - 2 * target, Width = 2 * target };
            time = inner with { Width = controls.Left - inner.Left };
        }
        else
        {
            var width = 2 * target + 2 * padding;
            var left = Math.Clamp(vault.Right + padding, safe.Left, safe.Right - width);
            var bottom = Math.Min(vault.Bottom, safe.Bottom);
            var top = Math.Max(safe.Top, Math.Min(vault.Top, bottom - metrics.Dp(80)));
            panel = new(left, top, width, bottom - top);
            var inner = panel.Inset(padding);
            var controlsHeight = inner.Height * .42f;
            time = inner with { Height = inner.Height - controlsHeight };
            controls = inner with { Top = time.Bottom, Height = controlsHeight };
        }
        var hitTop = Math.Min(controls.Top, safe.Bottom - target);
        var hitHeight = safe.Bottom - hitTop;
        var half = controls.Width / 2;
        return new(
            panel,
            time,
            controls,
            new(controls.Left, hitTop, half, hitHeight),
            new(controls.Left + half, hitTop, half, hitHeight)
        );
    }

    /// <summary>The tallest the one-row clock under the vault gets.</summary>
    public const float RowHeightDp = 52f;

    /// <summary>The time's width in the one-row clock.</summary>
    public const float RowTimeWidthDp = 96f;
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
)
{
    /// <summary>The time and the controls side by side in one row, rather than stacked.</summary>
    public bool Row => Controls.Left >= Time.Right - 0.01f;
}
