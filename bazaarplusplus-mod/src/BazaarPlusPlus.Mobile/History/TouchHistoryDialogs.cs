using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Ui;
using BazaarPlusPlus.Storage.RunLog;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.History;

/// <summary>
/// History's More dialog on a mobile platform, the desktop's <c>HistoryPanelView</c> More dialog on the
/// touch layout: the database status with the server health probe, the status line (the rows
/// below rise into it while it is empty), and the BazaarDB account row with its link form. The
/// dialog is as tall as its rows and drag-scrolls only when the form outgrows a short screen.
/// Supporter attribution lives in the main header. The code is read from
/// the input when the user submits it, so the input needs no change listener.
/// </summary>
internal sealed class TouchHistoryMoreDialog : IHistoryOverlay
{
    private readonly GameObject _root;
    private readonly HistoryTouchLayout _layout;
    private readonly Actions _actions;
    private readonly UnityEngine.UI.ScrollRect _scroll;
    private readonly RectTransform _dialog;
    private readonly TextMeshProUGUI _database;
    private readonly HistoryButton _health;
    private readonly TextMeshProUGUI _status;
    private readonly RectTransform _below;
    private readonly TextMeshProUGUI _accountTitle;
    private readonly HistoryButton _accountToggle;
    private readonly TextMeshProUGUI _accountStatus;
    private readonly TextMeshProUGUI _accountPersistence;
    private readonly GameObject _form;
    private readonly TextMeshProUGUI _accountWhy;
    private readonly TMP_InputField _accountInput;
    private readonly HistoryButton _accountSubmit;
    private readonly TextMeshProUGUI _accountBanner;
    private readonly HistoryButton _accountLinked;
    private string? _inputAccount;
    private (bool Form, bool Status)? _shownRows;

    public TouchHistoryMoreDialog(
        RectTransform parent,
        TMP_FontAsset font,
        HistoryTouchLayout layout,
        Actions actions
    )
    {
        _layout = layout;
        _actions = actions;
        var root = TouchHistoryUi.Overlay(
            parent,
            "More",
            "more_outside",
            () => Close("outside_tap")
        );
        _root = root.gameObject;
        var dialog = TouchHistoryUi.Panel(root, "Dialog", layout.Dialog);
        _dialog = dialog;
        var origin = layout.Dialog;
        var title = TouchHistoryUi.Label(
            dialog,
            font,
            "Title",
            Local(layout.DialogTitle, origin),
            layout.TitleFont
        );
        title.text = TouchHistoryUi.T("History options", "历史记录选项");
        TouchHistoryUi.Button(
            dialog,
            font,
            "more_close",
            Local(layout.DialogClose, origin),
            layout.TitleFont,
            () => Close("close_button"),
            UiKit.CloseGlyph
        );
        var (scroll, content) = TouchHistoryUi.Scroll(
            dialog,
            "Rows",
            Local(layout.MoreScroll, origin),
            horizontal: false
        );
        _scroll = scroll;
        _database = TouchHistoryUi.Label(
            content,
            font,
            "Database",
            layout.MoreDatabase,
            layout.BodyFont,
            wrap: true
        );
        _health = TouchHistoryUi.Button(
            content,
            font,
            "server_health",
            layout.MoreHealth,
            layout.ButtonFont,
            () => _actions.CheckHealth()
        );
        _status = TouchHistoryUi.Label(
            content,
            font,
            "Status",
            layout.MoreStatus,
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            wrap: true
        );
        // The rows under the status line, moved as one while that line is empty.
        _below = UiKit.Rect(
            "Below",
            content,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        var below = _below;
        _accountTitle = TouchHistoryUi.Label(
            below,
            font,
            "AccountTitle",
            layout.AccountTitle,
            layout.RowTitleFont
        );
        _accountToggle = TouchHistoryUi.Button(
            below,
            font,
            "account_toggle",
            layout.AccountToggle,
            layout.ButtonFont,
            () => _actions.ToggleAccountForm()
        );
        _accountStatus = TouchHistoryUi.Label(
            below,
            font,
            "AccountStatus",
            layout.AccountStatus,
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            TouchHistoryUi.Muted,
            wrap: true
        );
        _accountPersistence = TouchHistoryUi.Label(
            below,
            font,
            "AccountPersistence",
            layout.AccountPersistence,
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            TouchHistoryUi.Muted,
            wrap: true
        );
        var form = UiKit.Rect(
            "AccountForm",
            below,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        _form = form.gameObject;
        _accountWhy = TouchHistoryUi.Label(
            form,
            font,
            "AccountWhy",
            layout.AccountWhy,
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            TouchHistoryUi.Muted,
            wrap: true
        );
        _accountInput = TouchHistoryUi.Input(
            form,
            font,
            "AccountCode",
            layout.AccountInput,
            layout.ButtonFont,
            HistoryPanelText.AccountLink.EmptyCode(),
            10,
            multiLine: false
        );
        _accountSubmit = TouchHistoryUi.Button(
            form,
            font,
            "account_submit",
            layout.AccountSubmit,
            layout.ButtonFont,
            Submit
        );
        _accountBanner = TouchHistoryUi.Label(
            form,
            font,
            "AccountBanner",
            layout.AccountBanner,
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            wrap: true
        );
        _accountLinked = TouchHistoryUi.Button(
            form,
            font,
            "account_linked",
            layout.AccountLinked,
            layout.ButtonFont,
            () => _actions.MarkLinked()
        );
        _root.SetActive(false);
    }

    public bool IsOpen => _root.activeSelf;

    public void Open()
    {
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        _scroll.StopMovement();
        _scroll.content.anchoredPosition = Vector2.zero;
        BppLog.InfoEvent(new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.more_opened"));
    }

    /// <summary>False when it was not open.</summary>
    public bool Close(string reason)
    {
        if (!IsOpen)
            return false;
        _accountInput.DeactivateInputField(false);
        _root.SetActive(false);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.more_closed"),
            ("reason", reason)
        );
        return true;
    }

    public void Refresh(HistoryPanelViewModel m)
    {
        if (_inputAccount != m.AccountId)
        {
            _inputAccount = m.AccountId;
            _accountInput.SetTextWithoutNotify(string.Empty);
            _accountInput.DeactivateInputField(false);
        }
        _database.text = m.DatabaseChipText;
        _database.color = TouchHistoryUi.StatusColor(m.DatabaseChipSeverity);
        _health.Set(m.ServerHealthButtonText, enabled: m.ServerHealthButtonEnabled);
        _status.text = m.StatusMessage ?? string.Empty;
        _status.color = TouchHistoryUi.StatusColor(m.StatusSeverity);
        var hasStatus = _status.text.Length > 0;
        TouchHistoryUi.SetActive(_status.gameObject, hasStatus);
        _accountTitle.text = m.AccountTitleText;
        _accountStatus.text = m.AccountRowStatusText;
        _accountPersistence.text = m.AccountPersistenceText;
        TouchHistoryUi.SetActive(_accountToggle.GameObject, m.AccountRowActionVisible);
        _accountToggle.Set(
            m.AccountLinkFormVisible ? m.AccountLinkCollapseText : m.AccountRowActionText
        );
        if (!m.AccountLinkFormVisible)
            _accountInput.DeactivateInputField(false);
        TouchHistoryUi.SetActive(_form, m.AccountLinkFormVisible);
        if (_shownRows != (m.AccountLinkFormVisible, hasStatus))
        {
            _shownRows = (m.AccountLinkFormVisible, hasStatus);
            var rise = hasStatus ? 0f : _layout.AccountTitle.Top - _layout.MoreStatus.Top;
            _below.anchoredPosition = new Vector2(0f, rise);
            var content =
                (m.AccountLinkFormVisible ? _layout.MoreContentHeight : _layout.AccountFormTop)
                - rise;
            _scroll.content.sizeDelta = new Vector2(_layout.MoreScroll.Width, content);
            Fit(content);
        }
        _accountWhy.text = m.AccountWhyText + "\n" + m.AccountHintText;
        _accountInput.interactable = m.AccountLinkInputEnabled;
        _accountSubmit.Set(m.AccountLinkButtonText, true, m.AccountLinkButtonEnabled);
        _accountBanner.text = m.AccountLinkBannerText ?? string.Empty;
        _accountBanner.color = TouchHistoryUi.StatusColor(m.AccountLinkBannerSeverity);
        TouchHistoryUi.SetActive(_accountLinked.GameObject, m.AccountAlreadyLinkedButtonVisible);
        _accountLinked.Set(m.AccountAlreadyLinkedButtonText);
    }

    // The dialog is as tall as its rows, centred where the full-height dialog would stand; only a
    // form taller than the sheet scrolls.
    private void Fit(float content)
    {
        var full = _layout.Dialog;
        var header = _layout.MoreScroll.Top - full.Top;
        var footer = full.Bottom - _layout.MoreScroll.Bottom;
        var height = Math.Min(full.Height, header + content + footer);
        TouchScreen.Place(
            _dialog,
            new TouchRect(full.Left, full.Top + (full.Height - height) / 2f, full.Width, height)
        );
        TouchScreen.Place(
            _scroll.GetComponent<RectTransform>(),
            new TouchRect(
                _layout.MoreScroll.Left - full.Left,
                header,
                _layout.MoreScroll.Width,
                height - header - footer
            )
        );
    }

    private void Submit()
    {
        var code = (_accountInput.text ?? string.Empty).Trim();
        _accountInput.DeactivateInputField(false);
        _actions.Redeem(code);
    }

    private static TouchRect Local(TouchRect rect, TouchRect origin) =>
        new(rect.Left - origin.Left, rect.Top - origin.Top, rect.Width, rect.Height);

    /// <summary>What the dialog's controls call; each runs inside the main-thread scope.</summary>
    public sealed record Actions(
        Action CheckHealth,
        Action ToggleAccountForm,
        Action<string> Redeem,
        Action MarkLinked
    );
}

/// <summary>
/// The favorite note editor on a mobile platform, the desktop's note dialog on the touch layout: the note
/// opens on the selected favorite and saves to the favorite it opened on, even if a reload
/// reselects another row meanwhile. A failed save keeps the draft; the status line says why. The
/// character count follows the field's change listener (<see cref="UiActions.OnChanged"/>);
/// nothing polls the field each frame.
/// </summary>
internal sealed class TouchHistoryNoteDialog : IHistoryOverlay
{
    private readonly GameObject _root;
    private readonly TMP_InputField _input;
    private readonly TextMeshProUGUI _count;
    private readonly HistoryButton _save;
    private readonly Func<string, string, Task<bool>> _saveNote;
    private string? _battleId;
    private bool _saving;
    private int _shownLength = -1;
    private bool _disposed;

    public TouchHistoryNoteDialog(
        RectTransform parent,
        TMP_FontAsset font,
        HistoryTouchLayout layout,
        Func<string, string, Task<bool>> saveNote
    )
    {
        _saveNote = saveNote;
        var root = TouchHistoryUi.Overlay(
            parent,
            "FavoriteNote",
            "note_outside",
            () => Close("outside_tap")
        );
        _root = root.gameObject;
        var dialog = TouchHistoryUi.Panel(root, "Dialog", layout.Dialog);
        var origin = layout.Dialog;
        var title = TouchHistoryUi.Label(
            dialog,
            font,
            "Title",
            Local(layout.DialogTitle, origin),
            layout.TitleFont
        );
        title.text = HistoryPanelText.FavoriteNoteTitle();
        TouchHistoryUi.Button(
            dialog,
            font,
            "note_close",
            Local(layout.DialogClose, origin),
            layout.TitleFont,
            () => Close("close_button"),
            UiKit.CloseGlyph
        );
        _input = TouchHistoryUi.Input(
            dialog,
            font,
            "Note",
            Local(layout.NoteInput, origin),
            layout.ButtonFont,
            HistoryPanelText.FavoriteNotePlaceholder(),
            RunLogSchema.FavoriteNoteMaxLength,
            multiLine: true
        );
        _count = TouchHistoryUi.Label(
            dialog,
            font,
            "Count",
            Local(layout.NoteCount, origin),
            layout.BodyFont,
            color: TouchHistoryUi.Muted
        );
        // The count follows each edit; nothing reads the field's text while it sits unchanged.
        UiActions.OnChanged(_input, "history.note_input", text => ShowCount(text.Length));
        TouchHistoryUi
            .Button(
                dialog,
                font,
                "note_cancel",
                Local(layout.NoteCancel, origin),
                layout.ButtonFont,
                () => Close("cancel_button")
            )
            .Set(HistoryPanelText.Cancel());
        _save = TouchHistoryUi.Button(
            dialog,
            font,
            "note_save",
            Local(layout.NoteSave, origin),
            layout.ButtonFont,
            () => _ = SaveAsync()
        );
        _save.Set(HistoryPanelText.SaveNote(), selected: true);
        _root.SetActive(false);
    }

    public bool IsOpen => _root.activeSelf;

    /// <summary>The favorite and the unsaved text of an open dialog, which a relayout carries over.</summary>
    public (string BattleId, string Text)? Draft =>
        IsOpen && _battleId is { } battleId ? (battleId, _input.text ?? string.Empty) : null;

    public void Open(string battleId, string note)
    {
        _battleId = battleId;
        _saving = false;
        _input.SetTextWithoutNotify(note);
        _shownLength = -1;
        // The field's text, not the note: its character limit may have cut a stored note that
        // the database counted in code points.
        ShowCount((_input.text ?? string.Empty).Length);
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.note_opened"),
            ("note_length", note.Length)
        );
    }

    /// <summary>False when it was not open.</summary>
    public bool Close(string reason)
    {
        if (!IsOpen)
            return false;
        _battleId = null;
        _saving = false;
        _input.DeactivateInputField(false);
        _input.SetTextWithoutNotify(string.Empty);
        _root.SetActive(false);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.note_closed"),
            ("reason", reason)
        );
        return true;
    }

    /// <summary>The favorite the dialog edits is gone (removed, or the section changed): drop the draft.</summary>
    public void Refresh(HistoryPanelViewModel m)
    {
        if (
            _battleId != null
            && !_saving
            && !m.PageLoading
            && (
                m.SectionMode != HistorySectionMode.Favorites
                || !m.VisibleBattles.Any(battle => battle.BattleId == _battleId)
            )
        )
            Close("favorite_gone");
    }

    // The count under the draft; the label is written only when the length changed.
    private void ShowCount(int length)
    {
        if (_disposed || length == _shownLength)
            return;
        _shownLength = length;
        _count.text = HistoryPanelText.FavoriteNoteCount(
            length,
            RunLogSchema.FavoriteNoteMaxLength
        );
    }

    public void Dispose() => _disposed = true;

    private async Task SaveAsync()
    {
        if (_saving || _battleId == null)
            return;
        var battleId = _battleId;
        var note = _input.text ?? string.Empty;
        _saving = true;
        _save.Set(HistoryPanelText.SaveNote(), selected: true, enabled: false);
        bool saved;
        try
        {
            saved = await _saveNote(battleId, note);
        }
        catch (Exception)
        {
            saved = false;
        }
        if (_disposed || _battleId != battleId)
            return;
        _saving = false;
        _save.Set(HistoryPanelText.SaveNote(), selected: true);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.note_saved"),
            ("saved", saved),
            ("note_length", note.Length)
        );
        if (saved)
            Close("saved");
    }

    private static TouchRect Local(TouchRect rect, TouchRect origin) =>
        new(rect.Left - origin.Left, rect.Top - origin.Top, rect.Width, rect.Height);
}
