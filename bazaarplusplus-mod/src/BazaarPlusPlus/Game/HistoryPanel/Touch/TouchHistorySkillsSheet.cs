#nullable enable
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Game.Touch;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel.Touch;

/// <summary>
/// The selected battle's skills side picker over the panel, one button per board: the desktop's
/// native board shows skills beside the items, which a phone's detail column has no room for.
/// Tapping a side opens the game's own skill overlay on its skills
/// (<see cref="TouchNativeSkillOverlay"/>); a side without skills is dimmed and takes
/// no tap. The picker is sized to the skills it shows each time it opens
/// (<see cref="HistoryTouchLayout.SkillsSheet"/>). Each skill rents
/// the game's own skill prefab while the sheet is open; a tap inspects it. The close button, a
/// tap outside, or the back key dismisses the sheet, and its leases go back to the rental.
/// </summary>
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
        var root = TouchPanelUi.Overlay(
            parent,
            "Skills",
            "skills_outside",
            () => Close("outside_tap")
        );
        _root = root.gameObject;
        // Built at the largest size; Open places every part for what it shows.
        var full = layout.SkillsSheet([
            HistoryTouchLayout.SkillsPerRow,
            HistoryTouchLayout.SkillsPerRow,
        ]);
        var origin = full.Sheet;
        var sheet = TouchPanelUi.Panel(root, "Sheet", origin);
        _sheet = sheet;
        _title = TouchPanelUi.Label(
            sheet,
            font,
            "Title",
            Local(full.Title, origin),
            layout.TitleFont
        );
        _close = TouchPanelUi
            .Button(
                sheet,
                font,
                "skills_close",
                Local(full.Close, origin),
                layout.TitleFont,
                () => Close("close_button"),
                TouchUiKit.CloseGlyph
            )
            .Button.GetComponent<RectTransform>();
        for (var row = 0; row < 2; row++)
        {
            var side = row;
            // The side's button first, so its title and skills draw over it.
            _sides[row] = TouchPanelUi.Button(
                sheet,
                font,
                "skills_side",
                Local(full.Sides[row], origin),
                layout.BodyFont,
                () => pick(side)
            );
            TouchPanelUi.SetActive(_sides[row].Label.gameObject, false);
            _rowTitles[row] = TouchPanelUi.Label(
                sheet,
                font,
                $"Row{row}",
                Local(full.RowTitles[row], origin),
                layout.BodyFont,
                color: TouchPanelUi.Muted
            );
            for (var index = 0; index < HistoryTouchLayout.SkillsPerRow; index++)
            {
                var rect = TouchScreen.Child(
                    $"Skill{row}.{index}",
                    sheet,
                    Local(full.Cells[row, index], origin)
                );
                var slot = TouchUiKit.Rect(
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
                // A skill's tap is its side's: the overlay shows all of that side's skills.
                TouchPanelUi.TapArea(rect, "skill", () => pick(side));
                rect.gameObject.SetActive(false);
                _cells[row, index] = cell;
            }
        }
        _hint = TouchPanelUi.Label(
            sheet,
            font,
            "Hint",
            Local(full.Hint, origin),
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            TouchPanelUi.Muted,
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

    /// <summary>Opens the sheet on one or two rows of skills, each with its title.</summary>
    public void Open(IReadOnlyList<(string Title, IReadOnlyList<BppItemBoardCard> Skills)> rows)
    {
        ReturnAll();
        _title.text = TouchPanelUi.T("Skills", "技能");
        var fit = _layout.SkillsSheet(rows.Select(row => row.Skills.Count).ToArray());
        Place(fit);
        var shown = 0;
        for (var row = 0; row < 2; row++)
        {
            var has = row < rows.Count;
            TouchPanelUi.SetActive(_rowTitles[row].gameObject, has);
            TouchPanelUi.SetActive(_sides[row].GameObject, has);
            var skills = has ? rows[row].Skills : Array.Empty<BppItemBoardCard>();
            if (has)
            {
                _rowTitles[row].text =
                    skills.Count == 0
                        ? $"{rows[row].Title} · {TouchPanelUi.T("No skills", "无技能")}"
                        : $"{rows[row].Title} · {skills.Count}";
                _rowTitles[row].color = skills.Count == 0 ? TouchPanelUi.Muted : TouchPanelUi.Ink;
                _sides[row].Set(string.Empty, enabled: skills.Count > 0);
            }
            for (var index = 0; index < HistoryTouchLayout.SkillsPerRow; index++)
            {
                var cell = _cells[row, index];
                var skill = index < skills.Count ? skills[index] : null;
                if (skill != null)
                    TouchScreen.Place(cell.Root, Local(fit.Cells[row, index], fit.Sheet));
                TouchPanelUi.SetActive(cell.Root.gameObject, skill != null);
                if (skill == null)
                    continue;
                shown++;
                cell.Lease = TouchCardRental.Rent(
                    new CardRentalSubject(skill.TemplateId, skill.Tier, null),
                    cell.Slot,
                    RentalOwner
                );
            }
        }
        var hidden = rows.Sum(row =>
            Math.Max(0, row.Skills.Count - HistoryTouchLayout.SkillsPerRow)
        );
        _hint.text =
            (
                hidden > 0
                    ? TouchPanelUi.T($"{hidden} more not shown. ", $"另有 {hidden} 个未显示。")
                    : ""
            ) + TouchPanelUi.T("Pick a side to see its skills.", "选择一方查看技能详情。");
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.skills_opened"),
            ("rows", rows.Count),
            ("skills", shown),
            ("skill_side", Math.Round(_layout.SkillSide / _layout.Metrics.UnitsPerDp, 1))
        );
    }

    /// <summary>False when it was not open.</summary>
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
            ("rental_leases", TouchCardRental.ActiveLeases)
        );
        return true;
    }

    void IHistoryCardOverlay.ReturnCards() => ReturnAll();

    /// <summary>Every skill goes back to the rental; the sheet stays as it is.</summary>
    public int ReturnAll()
    {
        var returned = 0;
        foreach (var cell in _cells)
        {
            if (cell.Lease == null)
                continue;
            TouchCardRental.Return(cell.Lease);
            cell.Lease = null;
            returned++;
        }
        return returned;
    }

    // The sheet and its fixed parts; the shown cells are placed as they are filled.
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

        public CardRentalLease<CardPreviewBase>? Lease { get; set; }
    }
}
