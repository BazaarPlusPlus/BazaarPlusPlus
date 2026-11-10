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
        float CutoutLeft = 0f
    );

    private static readonly Screen[] Screens =
    [
        new("2400x1080@420", 2400f, 1080f, 420f),
        new("2560x1600@420", 2560f, 1600f, 420f),
        new("1920x1080@420", 1920f, 1080f, 420f),
        new("2400x1080@420 cutout", 2400f, 1080f, 420f, CutoutLeft: 110f),
        new("2340x1080@440", 2340f, 1080f, 440f),
        new("2400x1080 unknown dpi", 2400f, 1080f, 0f),
        new("2670x1200@480 cutout", 2670f, 1200f, 480f, CutoutLeft: 134f),
        new("640x360@160", 640f, 360f, 160f),
        new("1920x1080@560 cutout", 1920f, 1080f, 560f, CutoutLeft: 110f),
    ];

    public static void Run()
    {
        foreach (var screen in Screens)
            Check(screen);
        CheckNarrowHeader();
        var navigation = new HistoryTouchNavigation();
        Require(!navigation.Back(), "The archive has no detail back step.");
        navigation.ArchiveScrollDp[0] = 120f;
        navigation.PlayerBoard = true;
        navigation.OpenDetail();
        Require(
            navigation.Back() && !navigation.DetailOpen,
            "Back returns from detail to records."
        );
        Require(
            navigation.ArchiveScrollDp[0] == 120f && navigation.PlayerBoard,
            "Returning preserves the archive offset and chosen board side."
        );
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

        RequireLaidOut(
            name + " header",
            layout.Sheet,
            [
                ("tabs", layout.TabsTray),
                ("filter", layout.ArchiveFilter),
                ("support", layout.Sponsor),
                ("more", layout.More),
                ("close", layout.Close),
            ]
        );
        Require(
            layout.TabsTray.Bottom <= layout.Archive.Top
                && layout.TabsTray.Right <= layout.Archive.Right + .01f,
            $"{name}: the section tabs head the archive column from the header row."
        );
        RequireLaidOut(
            name + " archive",
            layout.Archive,
            [
                ("list", layout.ArchiveList),
                ("page", layout.PageLabel),
                ("newer", layout.PagerButton(0)),
                ("latest", layout.PagerButton(1)),
                ("older", layout.PagerButton(2)),
            ]
        );
        Require(
            layout.PageLabel.Width >= metrics.Dp(HistoryTouchLayout.PageLabelMinDp) - .01f,
            $"{name}: the page label keeps a readable width ({layout.PageLabel})."
        );
        Require(
            layout.PageLabelInline == Near(layout.PageLabel.Top, layout.ArchivePager.Top),
            $"{name}: an inline page label shares the pager row."
        );
        var visibleRows = layout.ArchiveList.Height / layout.RunRowHeight;
        Require(
            layout.Compact || visibleRows >= 3.5f,
            $"{name}: the archive beside the detail shows at least three and a half rows ({visibleRows:0.00})."
        );
        RequireLaidOut(
            name + " tabs",
            layout.TabsTray,
            [
                ("runs", layout.RunsTab),
                ("ghost", layout.GhostTab),
                ("favorites", layout.FavoritesTab),
            ]
        );
        var detail = new List<(string, TouchRect)>
        {
            ("upper title", layout.UpperBoardTitle),
            ("upper board", layout.UpperBoard),
            ("delete", layout.Delete),
            ("favorite", layout.Favorite),
            ("skills", layout.Skills),
            ("replay", layout.Replay),
        };
        if (layout.Compact)
        {
            detail.Add(("back", layout.Back));
            detail.Add(("battles", layout.Rail));
            detail.Add(("opponent", layout.OpponentToggle));
            detail.Add(("you", layout.PlayerToggle));
            Require(
                layout.LowerBoard == layout.UpperBoard,
                $"{name}: compact sides share one readable board instead of shrinking two."
            );
        }
        else
        {
            detail.Add(("lower title", layout.LowerBoardTitle));
            detail.Add(("lower board", layout.LowerBoard));
            RequireLaidOut(
                name + " columns",
                layout.Sheet,
                [("archive", layout.Archive), ("rail", layout.Rail), ("detail", layout.Detail)]
            );
        }
        RequireLaidOut(name + " detail", layout.Detail, detail.ToArray());
        RequireLaidOut(
            name + " paged battles",
            layout.Rail,
            [("newer", layout.RailNewer), ("list", layout.RailList), ("older", layout.RailOlder)]
        );
        foreach (
            var target in new[]
            {
                layout.RunsTab,
                layout.GhostTab,
                layout.FavoritesTab,
                layout.Sponsor,
                layout.More,
                layout.Close,
                layout.ArchiveFilter,
                layout.PagerButton(0),
                layout.PagerButton(1),
                layout.PagerButton(2),
                layout.RailNewer,
                layout.RailOlder,
                layout.RailChip(0),
                layout.Delete,
                layout.Favorite,
                layout.Skills,
                layout.Replay,
                layout.ArchiveRow(0, true),
                layout.ArchiveRow(0, false),
                layout.InspectClose,
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
            layout.Favorite.Left - layout.Delete.Right >= 3f * layout.Gap - .01f,
            $"{name}: delete remains separate from the other actions."
        );
        Require(
            layout.SocketWidth >= metrics.MinTarget - .01f,
            $"{name}: native small cards keep a 48dp target ({layout.SocketWidth / metrics.UnitsPerDp:0.0}dp)."
        );
        Require(
            Near(layout.CarpetWidth, 10f * layout.SocketWidth),
            $"{name}: ten sockets define the visible board edge."
        );
        var lastCard = layout.CardRect(9, 3);
        Require(
            lastCard.Right <= layout.CarpetWidth + .01f
                && Near(lastCard.Width, 3 * layout.SocketWidth),
            $"{name}: an oversized starting socket clamps onto the board."
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
            var scale = layout.InspectScale(span);
            Require(
                NativeCardPrefab.SocketWidth * span * scale <= layout.InspectCardArea.Width + 0.01f
                    && NativeCardPrefab.Height * NativeCardPrefab.FrameHeightOverSocket * scale
                        <= layout.InspectCardArea.Height + 0.01f,
                $"{name}: the enlarged card fits its area."
            );
        }

        // The skills sheet hugs what it shows, from one skill to two full rows.
        foreach (
            var counts in new[]
            {
                new[] { 1 },
                new[] { 3, 0 },
                new[] { 0, 3 },
                new[] { HistoryTouchLayout.SkillsPerRow, HistoryTouchLayout.SkillsPerRow },
            }
        )
        {
            var sheet = layout.SkillsSheet(counts);
            var label = $"{name} skills {string.Join("/", counts)}";
            var picker = new List<(string, TouchRect)>
            {
                ("title", sheet.Title),
                ("close", sheet.Close),
                ("hint", sheet.Hint),
            };
            for (var row = 0; row < counts.Length; row++)
            {
                picker.Add(($"side {row}", sheet.Sides[row]));
                var side = new List<(string, TouchRect)> { ("title", sheet.RowTitles[row]) };
                for (var index = 0; index < counts[row]; index++)
                    side.Add(($"skill {index}", sheet.Cells[row, index]));
                RequireLaidOut($"{label} side {row}", sheet.Sides[row], side.ToArray());
                if (counts[row] > 0)
                    RequireTarget(label, metrics, sheet.Sides[row]);
            }
            Require(layout.Sheet.Contains(sheet.Sheet), $"{label}: the sheet stays on the panel.");
            RequireLaidOut(label, sheet.Sheet, picker.ToArray());
            RequireTarget(label, metrics, sheet.Close);
        }
        Require(
            layout.SkillsSheet([1]).Sheet.Width < layout.Sheet.Width * .75f,
            $"{name}: one skill opens a small sheet, not the whole panel."
        );
        Require(
            layout.SkillsSheet([1, 0]).Sheet.Height < layout.SkillsSheet([1, 1]).Sheet.Height,
            $"{name}: a row without skills keeps only its title."
        );
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

    private static void CheckNarrowHeader()
    {
        var metrics = TouchMetrics.From(1920f, 1080f, 560f, 1080f, 110f, 0f, 1810f, 1080f);
        var layout = HistoryTouchLayout.For(metrics);
        Require(layout.Compact, "A narrow safe area opens details separately.");
        RequireLaidOut(
            "narrow header",
            layout.Sheet,
            [
                ("tabs", layout.TabsTray),
                ("filter", layout.ArchiveFilter),
                ("support", layout.Sponsor),
                ("more", layout.More),
                ("close", layout.Close),
            ]
        );
        foreach (
            var target in new[]
            {
                layout.RunsTab,
                layout.GhostTab,
                layout.FavoritesTab,
                layout.Sponsor,
                layout.ArchiveFilter,
                layout.More,
                layout.Close,
            }
        )
            RequireTarget("narrow header", metrics, target);
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
