using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar.UI;
using Il2CppTMPro;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The selected battle's skills over the panel, one row per board: the desktop's native board
/// shows them beside the items, which a phone's detail column has no room for. Each skill rents
/// the game's own skill prefab while the sheet is open; a tap inspects it. The close button, a
/// tap outside, or the back key dismisses the sheet, and its leases go back to the rental.
/// </summary>
internal sealed class AndroidHistorySkillsSheet : IHistoryCardOverlay
{
    public const string RentalOwner = "history.skills";

    private readonly GameObject _root;
    private readonly HistoryTouchLayout _layout;
    private readonly TextMeshProUGUI _title;
    private readonly TextMeshProUGUI[] _rowTitles = new TextMeshProUGUI[2];
    private readonly Cell[,] _cells = new Cell[2, HistoryTouchLayout.SkillsPerRow];
    private readonly TextMeshProUGUI _hint;

    public AndroidHistorySkillsSheet(
        RectTransform parent,
        TMP_FontAsset font,
        HistoryTouchLayout layout,
        Action<BppItemBoardCard> inspect
    )
    {
        _layout = layout;
        var root = AndroidHistoryUi.Overlay(
            parent,
            "Skills",
            "skills_outside",
            () => Close("outside_tap")
        );
        _root = root.gameObject;
        var sheet = AndroidHistoryUi.Panel(root, "Sheet", layout.Sheet);
        var origin = layout.Sheet;
        _title = AndroidHistoryUi.Label(
            sheet,
            font,
            "Title",
            Local(layout.SkillsTitle, origin),
            layout.TitleFont
        );
        AndroidHistoryUi.Button(
            sheet,
            font,
            "skills_close",
            Local(layout.SkillsClose, origin),
            layout.TitleFont,
            () => Close("close_button"),
            AndroidUiKit.CloseGlyph
        );
        for (var row = 0; row < 2; row++)
        {
            _rowTitles[row] = AndroidHistoryUi.Label(
                sheet,
                font,
                $"Row{row}",
                Local(layout.SkillRowTitle(row), origin),
                layout.BodyFont,
                color: AndroidHistoryUi.Muted
            );
            for (var index = 0; index < HistoryTouchLayout.SkillsPerRow; index++)
            {
                var rect = AndroidTouchScreen.Child(
                    $"Skill{row}.{index}",
                    sheet,
                    Local(layout.SkillCell(row, index), origin)
                );
                var slot = AndroidUiKit.Rect(
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
                AndroidHistoryUi.TapArea(
                    rect,
                    "skill",
                    () =>
                    {
                        if (cell.Card is { } card)
                            inspect(card);
                    },
                    AndroidHistoryUi.ButtonFill
                );
                rect.gameObject.SetActive(false);
                _cells[row, index] = cell;
            }
        }
        _hint = AndroidHistoryUi.Label(
            sheet,
            font,
            "Hint",
            Local(layout.SkillsHint, origin),
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            AndroidHistoryUi.Muted,
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
        _title.text = AndroidHistoryUi.T("Skills", "技能");
        var shown = 0;
        for (var row = 0; row < 2; row++)
        {
            var has = row < rows.Count;
            AndroidHistoryUi.SetActive(_rowTitles[row].gameObject, has);
            var skills = has ? rows[row].Skills : Array.Empty<BppItemBoardCard>();
            if (has)
                _rowTitles[row].text =
                    skills.Count == 0
                        ? $"{rows[row].Title} · {AndroidHistoryUi.T("No skills", "无技能")}"
                        : $"{rows[row].Title} · {skills.Count}";
            for (var index = 0; index < HistoryTouchLayout.SkillsPerRow; index++)
            {
                var cell = _cells[row, index];
                var skill = index < skills.Count ? skills[index] : null;
                cell.Card = skill;
                AndroidHistoryUi.SetActive(cell.Root.gameObject, skill != null);
                if (skill == null)
                    continue;
                shown++;
                cell.Lease = AndroidCardRental.Rent(
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
                    ? AndroidHistoryUi.T($"{hidden} more not shown. ", $"另有 {hidden} 个未显示。")
                    : ""
            ) + AndroidHistoryUi.T("Tap a skill to inspect it.", "点按技能查看大图。");
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.skills_opened"),
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
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.skills_closed"),
            ("reason", reason),
            ("returned", returned),
            ("rental_leases", AndroidCardRental.ActiveLeases)
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
            AndroidCardRental.Return(cell.Lease);
            cell.Lease = null;
            returned++;
        }
        return returned;
    }

    private static Core.Ui.TouchRect Local(Core.Ui.TouchRect rect, Core.Ui.TouchRect origin) =>
        new(rect.Left - origin.Left, rect.Top - origin.Top, rect.Width, rect.Height);

    private sealed class Cell(RectTransform root, RectTransform slot)
    {
        public RectTransform Root { get; } = root;

        public RectTransform Slot { get; } = slot;

        public BppItemBoardCard? Card { get; set; }

        public CardRentalLease<CardPreviewBase>? Lease { get; set; }
    }
}
