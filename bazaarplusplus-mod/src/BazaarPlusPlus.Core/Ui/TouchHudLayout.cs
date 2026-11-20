#nullable enable
namespace BazaarPlusPlus.Core.Ui;

public static class TouchHudLayout
{
    // Using the gear's width as a minimum keeps the two corner hit areas separate.
    public static TouchRect ShortcutBeside(TouchMetrics metrics, TouchRect gear)
    {
        var target = ShortcutSize(metrics, gear);
        var safe = metrics.SafeArea;
        var top = Math.Clamp(gear.CenterY - target / 2, safe.Top, safe.Bottom - target);
        var right = Math.Min(gear.Left, ShortcutBelow(metrics, gear).Left) - metrics.Dp(4);
        return new TouchRect(Math.Max(safe.Left, right - target), top, target, target);
    }

    public static TouchRect ShortcutInRow(TouchMetrics metrics, TouchRect gear, int index)
    {
        var target = ShortcutSize(metrics, gear);
        var safe = metrics.SafeArea;
        var gap = metrics.Dp(4);
        var top = Math.Clamp(gear.CenterY - target / 2, safe.Top, safe.Bottom - target);
        var right = gear.Left - gap - index * (target + gap);
        return new TouchRect(Math.Max(safe.Left, right - target), top, target, target);
    }

    // When Screen Fill gathers the HUD, the board reaches nearly to the gear and leaves no room below it.
    public static TouchRect Shortcut(TouchMetrics metrics, TouchRect gear, int index, bool inRun) =>
        inRun && ScreenFill.GathersHudAbove(metrics.CanvasWidth / metrics.CanvasHeight)
            ? ShortcutInRow(metrics, gear, index)
        : index == 0 ? ShortcutBelow(metrics, gear)
        : ShortcutBeside(metrics, gear);

    public static TouchRect ShortcutBelow(TouchMetrics metrics, TouchRect gear)
    {
        var target = ShortcutSize(metrics, gear);
        var safe = metrics.SafeArea;
        var left = Math.Clamp(gear.CenterX - target / 2, safe.Left, safe.Right - target);
        var top = Math.Max(safe.Top, gear.Bottom + metrics.Dp(4));
        return new TouchRect(left, top, target, target);
    }

    private static float ShortcutSize(TouchMetrics metrics, TouchRect gear) =>
        Math.Max(metrics.MinTarget, Math.Max(gear.Width, gear.Height));

    public static bool FitsBesideVault(TouchMetrics metrics, TouchRect vault) =>
        float.IsFinite(vault.Left)
        && float.IsFinite(vault.Top)
        && float.IsFinite(vault.Width)
        && float.IsFinite(vault.Height)
        && new TouchRect(0, 0, metrics.CanvasWidth, metrics.CanvasHeight).Contains(vault)
        && vault.CenterX > metrics.CanvasWidth / 2
        && vault.Height >= metrics.MinTarget;

    // When the clock sits beside the vault, extend hit areas toward the screen edge to meet the touch target floor.
    public static PlaybackLayout Playback(
        TouchMetrics metrics,
        TouchRect vault,
        float? boardTop = null
    )
    {
        var target = metrics.MinTarget;
        var padding = metrics.Dp(4);
        var safe = metrics.SafeArea;
        TouchRect time;
        TouchRect controls;
        TouchRect panel;
        var width = metrics.Dp(RowTimeWidthDp) + 2 * target + 2 * padding;
        var above = (boardTop ?? safe.Top) - safe.Top - 2 * padding;
        var below = safe.Bottom - vault.Bottom - padding;
        if (above >= target || below >= target)
        {
            if (above >= target)
            {
                var height = Math.Min(above, metrics.Dp(RowHeightDp));
                var top = safe.Top + padding + (above - height) / 2;
                var left = Math.Clamp(
                    metrics.CanvasWidth / 2 - width / 2,
                    safe.Left,
                    safe.Right - width
                );
                panel = new(left, top, width, height);
            }
            else
            {
                var height = Math.Min(below, metrics.Dp(RowHeightDp));
                var left = Math.Clamp(vault.CenterX - width / 2, safe.Left, safe.Right - width);
                panel = new(left, vault.Bottom + padding, width, height);
            }
            var inner = panel.Inset(padding);
            controls = inner with { Left = inner.Right - 2 * target, Width = 2 * target };
            time = inner with { Width = controls.Left - inner.Left };
        }
        else
        {
            width = 2 * target + 2 * padding;
            var left = Math.Clamp(vault.Right + padding, safe.Left, safe.Right - width);
            var bottom = Math.Min(vault.Bottom, safe.Bottom);
            var top = Math.Max(safe.Top, Math.Min(vault.Top, bottom - metrics.Dp(80)));
            panel = new(left, top, width, bottom - top);
            var inner = panel.Inset(padding);
            var controlsHeight = inner.Height * .42f;
            time = inner with { Height = inner.Height - controlsHeight };
            controls = inner with { Top = time.Bottom, Height = controlsHeight };
        }
        float hitTop;
        float hitHeight;
        if (controls.Left >= time.Right - 0.01f)
        {
            hitHeight = Math.Max(target, controls.Height);
            hitTop = Math.Clamp(
                controls.CenterY - hitHeight / 2,
                safe.Top,
                safe.Bottom - hitHeight
            );
        }
        else
        {
            hitTop = Math.Min(controls.Top, safe.Bottom - target);
            hitHeight = safe.Bottom - hitTop;
        }
        var half = controls.Width / 2;
        return new(
            panel,
            time,
            controls,
            new(controls.Left, hitTop, half, hitHeight),
            new(controls.Left + half, hitTop, half, hitHeight)
        );
    }

    public const float RowHeightDp = 52f;

    public const float RowTimeWidthDp = 96f;
}

public readonly record struct PlaybackLayout(
    TouchRect Panel,
    TouchRect Time,
    TouchRect Controls,
    TouchRect Speed,
    TouchRect Pause
)
{
    public bool Row => Controls.Left >= Time.Right - 0.01f;
}
