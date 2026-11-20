using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.History;

// The phone's detail column has no room for skills beside items, so open the native overlay
// from a side picker.
internal sealed class TouchHistorySkillsSheet : IHistoryCardOverlay
{
    public const string RentalOwner = "history.skills";

    private readonly GameObject _root;
    private readonly HistoryTouchLayout _layout;
    private readonly RectTransform _sheet;
    private readonly RectTransform _close;
    private readonly TextMeshProUGUI _title;
    private readonly HistoryButton[] _sides = new HistoryButton[2];
    private readonly TextMeshProUGUI[] _rowTitles = new TextMeshProUGUI[2];
    private readonly Cell[,] _cells = new Cell[2, HistoryTouchLayout.SkillsPerRow];
    private readonly TextMeshProUGUI _hint;

    public TouchHistorySkillsSheet(
        RectTransform parent,
        TMP_FontAsset font,
        HistoryTouchLayout layout,
        Action<int> pick
    )
    {
        _layout = layout;
        var root = TouchHistoryUi.Overlay(
            parent,
            "Skills",
            "skills_outside",
            () => Close("outside_tap")
        );
        _root = root.gameObject;
        var full = layout.SkillsSheet([
            HistoryTouchLayout.SkillsPerRow,
            HistoryTouchLayout.SkillsPerRow,
        ]);
        var origin = full.Sheet;
        var sheet = TouchHistoryUi.Panel(root, "Sheet", origin);
        _sheet = sheet;
        _title = TouchHistoryUi.Label(
            sheet,
            font,
            "Title",
            Local(full.Title, origin),
            layout.TitleFont
        );
        _close = TouchHistoryUi
            .Button(
                sheet,
                font,
                "skills_close",
                Local(full.Close, origin),
                layout.TitleFont,
                () => Close("close_button"),
                UiKit.CloseGlyph
            )
            .Button.GetComponent<RectTransform>();
        for (var row = 0; row < 2; row++)
        {
            var side = row;
            _sides[row] = TouchHistoryUi.Button(
                sheet,
                font,
                "skills_side",
                Local(full.Sides[row], origin),
                layout.BodyFont,
                () => pick(side)
            );
            TouchHistoryUi.SetActive(_sides[row].Label.gameObject, false);
            _rowTitles[row] = TouchHistoryUi.Label(
                sheet,
                font,
                $"Row{row}",
                Local(full.RowTitles[row], origin),
                layout.BodyFont,
                color: TouchHistoryUi.Muted
            );
            for (var index = 0; index < HistoryTouchLayout.SkillsPerRow; index++)
            {
                var rect = TouchScreen.Child(
                    $"Skill{row}.{index}",
                    sheet,
                    Local(full.Cells[row, index], origin)
                );
                var slot = UiKit.Rect(
                    "Slot",
                    rect,
                    new(0f, 1f),
                    new(0f, 1f),
                    Vector2.zero,
                    Vector2.zero
                );
                slot.anchoredPosition = new Vector2(layout.SkillSide / 2f, -layout.SkillSide / 2f);
                slot.localScale = new Vector3(layout.SkillScale, layout.SkillScale, 1f);
                var cell = new Cell(rect, slot);
                TouchHistoryUi.TapArea(rect, "skill", () => pick(side));
                rect.gameObject.SetActive(false);
                _cells[row, index] = cell;
            }
        }
        _hint = TouchHistoryUi.Label(
            sheet,
            font,
            "Hint",
            Local(full.Hint, origin),
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            TouchHistoryUi.Muted,
            wrap: true
        );
        _root.SetActive(false);
    }

    public bool IsOpen => _root.activeSelf;

    public int Leases
    {
        get
        {
            var leases = 0;
            foreach (var cell in _cells)
                if (cell.Lease != null)
                    leases++;
            return leases;
        }
    }

    public void Open(IReadOnlyList<(string Title, IReadOnlyList<BppItemBoardCard> Skills)> rows)
    {
        ReturnAll();
        _title.text = HistoryPanelText.Skills();
        var fit = _layout.SkillsSheet(rows.Select(row => row.Skills.Count).ToArray());
        Place(fit);
        var shown = 0;
        for (var row = 0; row < 2; row++)
        {
            var has = row < rows.Count;
            TouchHistoryUi.SetActive(_rowTitles[row].gameObject, has);
            TouchHistoryUi.SetActive(_sides[row].GameObject, has);
            var skills = has ? rows[row].Skills : Array.Empty<BppItemBoardCard>();
            if (has)
            {
                _rowTitles[row].text =
                    skills.Count == 0
                        ? $"{rows[row].Title} · {HistoryPanelText.NoSkills()}"
                        : $"{rows[row].Title} · {skills.Count}";
                _rowTitles[row].color =
                    skills.Count == 0 ? TouchHistoryUi.Muted : TouchHistoryUi.Ink;
                _sides[row].Set(string.Empty, enabled: skills.Count > 0);
            }
            for (var index = 0; index < HistoryTouchLayout.SkillsPerRow; index++)
            {
                var cell = _cells[row, index];
                var skill = index < skills.Count ? skills[index] : null;
                if (skill != null)
                    TouchScreen.Place(cell.Root, Local(fit.Cells[row, index], fit.Sheet));
                TouchHistoryUi.SetActive(cell.Root.gameObject, skill != null);
                if (skill == null)
                    continue;
                shown++;
                cell.Lease = MobilePlatform.Port.CardRental.Rent(
                    CardRentalSubject.Of(skill),
                    cell.Slot,
                    RentalOwner
                );
            }
        }
        var hidden = rows.Sum(row =>
            Math.Max(0, row.Skills.Count - HistoryTouchLayout.SkillsPerRow)
        );
        _hint.text =
            (hidden > 0 ? HistoryPanelText.SkillsHidden(hidden) : "") + HistoryPanelText.PickSide();
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.skills_opened"),
            ("rows", rows.Count),
            ("skills", shown),
            ("skill_side", Math.Round(_layout.SkillSide / _layout.Metrics.UnitsPerDp, 1))
        );
    }

    public bool Close(string reason)
    {
        if (!IsOpen)
            return false;
        var returned = ReturnAll();
        _root.SetActive(false);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.skills_closed"),
            ("reason", reason),
            ("returned", returned),
            ("rental_leases", MobilePlatform.Port.CardRental.ActiveLeases)
        );
        return true;
    }

    void IHistoryCardOverlay.ReturnCards() => ReturnAll();

    public int ReturnAll()
    {
        var returned = 0;
        foreach (var cell in _cells)
        {
            if (cell.Lease == null)
                continue;
            MobilePlatform.Port.CardRental.Return(cell.Lease);
            cell.Lease = null;
            returned++;
        }
        return returned;
    }

    private void Place(HistorySkillsSheetLayout fit)
    {
        TouchScreen.Place(_sheet, fit.Sheet);
        TouchScreen.Place(_title.rectTransform, Local(fit.Title, fit.Sheet));
        TouchScreen.Place(_close, Local(fit.Close, fit.Sheet));
        TouchScreen.Place(_hint.rectTransform, Local(fit.Hint, fit.Sheet));
        for (var row = 0; row < fit.RowTitles.Length; row++)
        {
            TouchScreen.Place(
                _sides[row].Button.GetComponent<RectTransform>(),
                Local(fit.Sides[row], fit.Sheet)
            );
            TouchScreen.Place(_rowTitles[row].rectTransform, Local(fit.RowTitles[row], fit.Sheet));
        }
    }

    private static Core.Ui.TouchRect Local(Core.Ui.TouchRect rect, Core.Ui.TouchRect origin) =>
        new(rect.Left - origin.Left, rect.Top - origin.Top, rect.Width, rect.Height);

    private sealed class Cell(RectTransform root, RectTransform slot)
    {
        public RectTransform Root { get; } = root;

        public RectTransform Slot { get; } = slot;

        public CardRentalLease<GameCardPreview>? Lease { get; set; }
    }
}
