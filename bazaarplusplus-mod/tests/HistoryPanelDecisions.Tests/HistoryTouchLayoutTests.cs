#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.HistoryPanel.Ui;

// HistoryTouchLayout on the screens the Android History panel must fit: a 20:9 phone, a 16:10
// tablet, a 16:9 phone, a 20:9 phone with a left cutout, and a denser 19.5:9 phone.
internal static class HistoryTouchLayoutTests
{
    private sealed record Screen(
        string Name,
        float Width,
        float Height,
        float Dpi,
        float CutoutLeft = 0f,
        bool FullSockets = true
    );

    private static readonly Screen[] Screens =
    [
        new("2400x1080@420", 2400f, 1080f, 420f),
        new("2560x1600@420", 2560f, 1600f, 420f),
        // Three columns cannot give ten 48dp sockets on a 731dp-wide canvas; everything else holds.
        new("1920x1080@420", 1920f, 1080f, 420f, FullSockets: false),
        new("2400x1080@420 cutout", 2400f, 1080f, 420f, CutoutLeft: 110f),
        new("2340x1080@440", 2340f, 1080f, 440f),
        new("2400x1080 unknown dpi", 2400f, 1080f, 0f),
    ];

    public static void Run()
    {
        foreach (var screen in Screens)
            Check(screen);
    }

    private static void Check(Screen screen)
    {
        var metrics = TouchMetrics.From(
            screen.Width,
            screen.Height,
            screen.Dpi,
            1080f,
            screen.CutoutLeft,
            0f,
            screen.Width - screen.CutoutLeft,
            screen.Height
        );
        var layout = HistoryTouchLayout.For(metrics);
        var name = screen.Name;

        Require(
            metrics.SafeArea.Contains(layout.Sheet),
            $"{name}: the sheet stays in the safe area."
        );
        foreach (
            var font in new[]
            {
                layout.TitleFont,
                layout.ButtonFont,
                layout.RowTitleFont,
                layout.BodyFont,
            }
        )
            Require(font >= metrics.BodyText - 0.01f, $"{name}: every font is at least 12sp.");

        var panel = new (string Name, TouchRect Rect)[]
        {
            ("runs tab", layout.RunsTab),
            ("ghost tab", layout.GhostTab),
            ("favorites tab", layout.FavoritesTab),
            ("filters", layout.FilterStrip),
            ("more", layout.More),
            ("close", layout.Close),
            ("archive list", layout.ArchiveList),
            ("page label", layout.PageLabel),
            ("pager 0", layout.PagerButton(0)),
            ("pager 1", layout.PagerButton(1)),
            ("pager 2", layout.PagerButton(2)),
            ("rail newer", layout.RailNewer),
            ("rail list", layout.RailList),
            ("rail older", layout.RailOlder),
            ("upper title", layout.UpperBoardTitle),
            ("upper board", layout.UpperBoard),
            ("lower title", layout.LowerBoardTitle),
            ("lower board", layout.LowerBoard),
            ("delete", layout.Delete),
            ("favorite", layout.Favorite),
            ("skills", layout.Skills),
            ("replay", layout.Replay),
            ("status", layout.Status),
        };
        RequireLaidOut(name, layout.Sheet, panel);

        foreach (
            var target in new[]
            {
                layout.RunsTab,
                layout.GhostTab,
                layout.FavoritesTab,
                layout.More,
                layout.Close,
                layout.PagerButton(0),
                layout.PagerButton(2),
                layout.RailNewer,
                layout.RailOlder,
                layout.Delete,
                layout.Favorite,
                layout.Skills,
                layout.Replay,
                layout.ArchiveRow(0, runs: true),
                layout.ArchiveRow(0, runs: false),
                layout.RailChip(0),
                layout.InspectClose,
                layout.SkillsClose,
                layout.SkillCell(0, 0),
                layout.DialogClose,
                layout.MoreHealth,
                layout.AccountToggle,
                layout.AccountInput,
                layout.AccountSubmit,
                layout.AccountLinked,
                layout.NoteCancel,
                layout.NoteSave,
            }
        )
            RequireTarget(name, metrics, target);
        Require(
            layout.FilterStrip.Height >= metrics.MinTarget - 0.01f,
            $"{name}: filter chips are targets."
        );
        foreach (
            var width in new[]
            {
                layout.HeroChipWidth,
                layout.OutcomeChipWidth,
                layout.DayChipWidth,
            }
        )
            Require(width >= metrics.MinTarget - 0.01f, $"{name}: a filter chip is a target wide.");
        Require(layout.RailList.Height >= layout.ChipHeight, $"{name}: the rail shows a battle.");
        Require(
            layout.ArchiveList.Height >= 2f * layout.RunRowHeight,
            $"{name}: the archive shows at least two runs."
        );

        Require(
            layout.Detail.Contains(layout.UpperBoard) && layout.Detail.Contains(layout.LowerBoard),
            $"{name}: both boards stay in the detail column."
        );
        Require(
            Near(layout.CarpetWidth, layout.SocketWidth * HistoryTouchLayout.SocketCount)
                && layout.CarpetWidth <= layout.Detail.Width + 0.01f,
            $"{name}: ten sockets fit the detail column."
        );
        var lastCard = layout.CardRect(HistoryTouchLayout.SocketCount - 1, 3);
        Require(
            lastCard.Right <= layout.CarpetWidth + 0.01f
                && Near(lastCard.Width, 3f * layout.SocketWidth),
            $"{name}: a large card at the last socket is pulled back onto the board."
        );
        if (screen.FullSockets)
            Require(
                layout.SocketWidth >= metrics.MinTarget - 0.01f,
                $"{name}: a small card is a full touch target ({layout.SocketWidth / metrics.UnitsPerDp:0.0}dp)."
            );
        else
            Require(
                layout.SocketWidth >= metrics.Dp(40f),
                $"{name}: a small card stays near a touch target."
            );

        var chips = layout.FilterChips([
            layout.HeroChipWidth,
            layout.OutcomeChipWidth,
            layout.DayChipWidth,
        ]);
        for (var index = 1; index < chips.Count; index++)
            Require(
                !chips[index].Overlaps(chips[index - 1])
                    && chips[index].Left > chips[index - 1].Left,
                $"{name}: filter chips stand side by side."
            );

        // Overlays: each fits the sheet and its controls do not overlap.
        RequireLaidOut(
            name + " inspection",
            layout.Sheet,
            [
                ("card", layout.InspectCardArea),
                ("name", layout.InspectName),
                ("close", layout.InspectClose),
                ("detail", layout.InspectDetail),
            ]
        );
        Require(
            layout.InspectInfo.Contains(layout.InspectDetail),
            $"{name}: inspection text stays in its panel."
        );
        foreach (var span in new[] { 1, 2, 3 })
        {
            var scale = layout.InspectScale(span, skill: false);
            Require(
                NativeCardPrefab.SocketWidth * span * scale <= layout.InspectCardArea.Width + 0.01f
                    && NativeCardPrefab.Height * NativeCardPrefab.FrameHeightOverSocket * scale
                        <= layout.InspectCardArea.Height + 0.01f,
                $"{name}: the enlarged card fits its area."
            );
        }

        var skillsSheet = new List<(string, TouchRect)>
        {
            ("title", layout.SkillsTitle),
            ("close", layout.SkillsClose),
        };
        for (var row = 0; row < 2; row++)
        {
            skillsSheet.Add(($"row {row} title", layout.SkillRowTitle(row)));
            for (var index = 0; index < HistoryTouchLayout.SkillsPerRow; index++)
                skillsSheet.Add(($"skill {row}.{index}", layout.SkillCell(row, index)));
        }
        RequireLaidOut(name + " skills", layout.Sheet, skillsSheet.ToArray());
        Require(
            Near(layout.SkillScale * NativeCardPrefab.Height, layout.SkillSide),
            $"{name}: a skill fills its square."
        );

        Require(layout.Sheet.Contains(layout.Dialog), $"{name}: the dialog stays on the sheet.");
        RequireLaidOut(
            name + " more",
            layout.Dialog,
            [
                ("title", layout.DialogTitle),
                ("close", layout.DialogClose),
                ("scroll", layout.MoreScroll),
            ]
        );
        var content = new TouchRect(0f, 0f, layout.MoreScroll.Width, layout.MoreContentHeight);
        RequireLaidOut(
            name + " more content",
            content,
            [
                ("database", layout.MoreDatabase),
                ("health", layout.MoreHealth),
                ("status", layout.MoreStatus),
                ("account title", layout.AccountTitle),
                ("account toggle", layout.AccountToggle),
                ("account status", layout.AccountStatus),
                ("persistence", layout.AccountPersistence),
                ("why", layout.AccountWhy),
                ("input", layout.AccountInput),
                ("submit", layout.AccountSubmit),
                ("banner", layout.AccountBanner),
                ("linked", layout.AccountLinked),
            ]
        );
        Require(
            Near(layout.AccountWhy.Top, layout.AccountFormTop),
            $"{name}: the account form starts where the account card ends."
        );
        RequireLaidOut(
            name + " note",
            layout.Dialog,
            [
                ("title", layout.DialogTitle),
                ("close", layout.DialogClose),
                ("input", layout.NoteInput),
                ("count", layout.NoteCount),
                ("cancel", layout.NoteCancel),
                ("save", layout.NoteSave),
            ]
        );
        Require(
            layout.NoteInput.Height >= metrics.Dp(96f),
            $"{name}: the note input has room for lines."
        );
    }

    private static void RequireLaidOut(
        string name,
        TouchRect bounds,
        (string Name, TouchRect Rect)[] rects
    )
    {
        foreach (var (label, rect) in rects)
        {
            Require(rect.Width > 0f && rect.Height > 0f, $"{name}: {label} has a size ({rect}).");
            Require(bounds.Contains(rect), $"{name}: {label} stays inside ({rect} in {bounds}).");
        }
        for (var i = 0; i < rects.Length; i++)
        for (var j = i + 1; j < rects.Length; j++)
            Require(
                !rects[i].Rect.Overlaps(rects[j].Rect),
                $"{name}: {rects[i].Name} and {rects[j].Name} do not overlap ({rects[i].Rect} / {rects[j].Rect})."
            );
    }

    private static void RequireTarget(string name, TouchMetrics metrics, TouchRect rect) =>
        Require(
            rect.Width >= metrics.MinTarget - 0.01f && rect.Height >= metrics.MinTarget - 0.01f,
            $"{name}: {rect} is at least a 48dp target."
        );

    private static bool Near(float left, float right) => Math.Abs(left - right) < 0.01f;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
