#nullable enable

namespace BazaarPlusPlus.Core.Ui;

/// <summary>
/// The Android host's touch entry, in canvas units top-down: the BPP button at the left edge of
/// the safe area and the menu it opens, a centered card that lists the panels the desktop opens
/// by hotkey (History, Collection, Ten-Win Builds) under a title row with its close button. Every
/// target is at least <see cref="TouchMetrics.MinTargetDp"/>; when the safe area is too short for
/// the comfortable row height, the rows shrink to that minimum and never further. The settings
/// themselves live in the game's own settings dialog, not here.
/// </summary>
public sealed class TouchLauncherLayout
{
    private const float MarginDp = 16f;
    private const float PaddingDp = 16f;
    private const float GapDp = 8f;
    private const float RowDp = 56f;
    private const float MenuWidthDp = 360f;
    private const float LauncherWidthDp = 64f;

    private TouchLauncherLayout(TouchMetrics metrics, int entryCount)
    {
        if (entryCount < 0)
            throw new ArgumentOutOfRangeException(nameof(entryCount));
        var safe = metrics.SafeArea;
        var margin = metrics.Dp(MarginDp);
        var padding = metrics.Dp(PaddingDp);
        var gap = metrics.Dp(GapDp);

        LauncherFont = metrics.Sp(14f);
        TitleFont = metrics.Sp(18f);
        EntryFont = metrics.Sp(16f);

        var launcherHeight = metrics.MinTarget;
        Launcher = new TouchRect(
            safe.Left + metrics.Dp(4f),
            safe.CenterY - launcherHeight / 2f,
            metrics.Target(LauncherWidthDp),
            launcherHeight
        );

        // One row for the title and the close button, then the entries; an empty menu keeps one
        // row for the hint that every panel is switched off.
        var rows = Math.Max(1, entryCount);
        var available = safe.Height - 2f * margin - 2f * padding - gap * rows;
        var row = Math.Max(metrics.MinTarget, Math.Min(metrics.Dp(RowDp), available / (rows + 1)));
        var width = Math.Min(safe.Width - 2f * margin, metrics.Dp(MenuWidthDp));
        var height = 2f * padding + row * (rows + 1) + gap * rows;
        Menu = new TouchRect(safe.CenterX - width / 2f, safe.CenterY - height / 2f, width, height);

        var body = Menu.Inset(padding);
        Close = new TouchRect(body.Right - row, body.Top, row, row);
        Title = new TouchRect(body.Left, body.Top, Math.Max(0f, body.Width - row - gap), row);
        var entries = new TouchRect[rows];
        for (var index = 0; index < rows; index++)
            entries[index] = new TouchRect(
                body.Left,
                body.Top + (row + gap) * (index + 1),
                body.Width,
                row
            );
        Entries = entries;
    }

    public static TouchLauncherLayout Compute(TouchMetrics metrics, int entryCount) =>
        new(metrics, entryCount);

    /// <summary>The BPP button, centered on the safe area's left edge.</summary>
    public TouchRect Launcher { get; }

    /// <summary>The menu card, centered in the safe area.</summary>
    public TouchRect Menu { get; }

    /// <summary>The menu's title, left of <see cref="Close"/>.</summary>
    public TouchRect Title { get; }

    public TouchRect Close { get; }

    /// <summary>One row per entry, top to bottom; an empty menu has one, for its hint.</summary>
    public IReadOnlyList<TouchRect> Entries { get; }

    public float LauncherFont { get; }

    public float TitleFont { get; }

    public float EntryFont { get; }
}
