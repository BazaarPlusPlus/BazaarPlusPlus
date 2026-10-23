using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Storage.RunLog;
using Il2CppTMPro;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// History's More dialog on Android, the desktop's <c>HistoryPanelView</c> More dialog on the
/// touch layout: the database status with the server health probe, the status line, and the
/// BazaarDB account row with its link form, drag-scrolled when the form outgrows a short screen.
/// The desktop's supporter list stays off: the host has no supporter feed. The code is read from
/// the input when the user submits it, so the input needs no change listener.
/// </summary>
internal sealed class AndroidHistoryMoreDialog
{
    private readonly GameObject _root;
    private readonly HistoryTouchLayout _layout;
    private readonly Actions _actions;
    private readonly UnityEngine.UI.ScrollRect _scroll;
    private readonly TextMeshProUGUI _database;
    private readonly HistoryButton _health;
    private readonly TextMeshProUGUI _status;
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
    private bool? _formShown;

    public AndroidHistoryMoreDialog(
        RectTransform parent,
        TMP_FontAsset font,
        HistoryTouchLayout layout,
        Actions actions
    )
    {
        _layout = layout;
        _actions = actions;
        var root = AndroidHistoryUi.Overlay(
            parent,
            "More",
            "more_outside",
            () => Close("outside_tap")
        );
        _root = root.gameObject;
        var dialog = AndroidHistoryUi.Panel(root, "Dialog", layout.Dialog);
        var origin = layout.Dialog;
        var title = AndroidHistoryUi.Label(
            dialog,
            font,
            "Title",
            Local(layout.DialogTitle, origin),
            layout.TitleFont
        );
        title.text = AndroidHistoryUi.T("History options", "历史记录选项");
        AndroidHistoryUi.Button(
            dialog,
            font,
            "more_close",
            Local(layout.DialogClose, origin),
            layout.TitleFont,
            () => Close("close_button"),
            AndroidUiKit.CloseGlyph
        );
        var (scroll, content) = AndroidHistoryUi.Scroll(
            dialog,
            "Rows",
            Local(layout.MoreScroll, origin),
            horizontal: false
        );
        _scroll = scroll;
        _database = AndroidHistoryUi.Label(
            content,
            font,
            "Database",
            layout.MoreDatabase,
            layout.BodyFont,
            wrap: true
        );
        _health = AndroidHistoryUi.Button(
            content,
            font,
            "server_health",
            layout.MoreHealth,
            layout.ButtonFont,
            () => _actions.CheckHealth()
        );
        _status = AndroidHistoryUi.Label(
            content,
            font,
            "Status",
            layout.MoreStatus,
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            wrap: true
        );
        _accountTitle = AndroidHistoryUi.Label(
            content,
            font,
            "AccountTitle",
            layout.AccountTitle,
            layout.RowTitleFont
        );
        _accountToggle = AndroidHistoryUi.Button(
            content,
            font,
            "account_toggle",
            layout.AccountToggle,
            layout.ButtonFont,
            () => _actions.ToggleAccountForm()
        );
        _accountStatus = AndroidHistoryUi.Label(
            content,
            font,
            "AccountStatus",
            layout.AccountStatus,
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            AndroidHistoryUi.Muted,
            wrap: true
        );
        _accountPersistence = AndroidHistoryUi.Label(
            content,
            font,
            "AccountPersistence",
            layout.AccountPersistence,
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            AndroidHistoryUi.Muted,
            wrap: true
        );
        var form = AndroidUiKit.Rect(
            "AccountForm",
            content,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        _form = form.gameObject;
        _accountWhy = AndroidHistoryUi.Label(
            form,
            font,
            "AccountWhy",
            layout.AccountWhy,
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            AndroidHistoryUi.Muted,
            wrap: true
        );
        _accountInput = AndroidHistoryUi.Input(
            form,
            font,
            "AccountCode",
            layout.AccountInput,
            layout.ButtonFont,
            HistoryPanelText.AccountLink.EmptyCode(),
            10,
            multiLine: false
        );
        _accountSubmit = AndroidHistoryUi.Button(
            form,
            font,
            "account_submit",
            layout.AccountSubmit,
            layout.ButtonFont,
            Submit
        );
        _accountBanner = AndroidHistoryUi.Label(
            form,
            font,
            "AccountBanner",
            layout.AccountBanner,
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            wrap: true
        );
        _accountLinked = AndroidHistoryUi.Button(
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
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.more_opened")
        );
    }

    /// <summary>False when it was not open.</summary>
    public bool Close(string reason)
    {
        if (!IsOpen)
            return false;
        _accountInput.DeactivateInputField(false);
        _root.SetActive(false);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.more_closed"),
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
        _database.color = AndroidHistoryUi.StatusColor(m.DatabaseChipSeverity);
        _health.Set(m.ServerHealthButtonText, enabled: m.ServerHealthButtonEnabled);
        _status.text = m.StatusMessage ?? string.Empty;
        _status.color = AndroidHistoryUi.StatusColor(m.StatusSeverity);
        _accountTitle.text = m.AccountTitleText;
        _accountStatus.text = m.AccountRowStatusText;
        _accountPersistence.text = m.AccountPersistenceText;
        AndroidHistoryUi.SetActive(_accountToggle.GameObject, m.AccountRowActionVisible);
        _accountToggle.Set(
            m.AccountLinkFormVisible ? m.AccountLinkCollapseText : m.AccountRowActionText
        );
        if (!m.AccountLinkFormVisible)
            _accountInput.DeactivateInputField(false);
        AndroidHistoryUi.SetActive(_form, m.AccountLinkFormVisible);
        if (_formShown != m.AccountLinkFormVisible)
        {
            _formShown = m.AccountLinkFormVisible;
            _scroll.content.sizeDelta = new Vector2(
                _layout.MoreScroll.Width,
                m.AccountLinkFormVisible ? _layout.MoreContentHeight : _layout.AccountFormTop
            );
        }
        _accountWhy.text = m.AccountWhyText + "\n" + m.AccountHintText;
        _accountInput.interactable = m.AccountLinkInputEnabled;
        _accountSubmit.Set(m.AccountLinkButtonText, true, m.AccountLinkButtonEnabled);
        _accountBanner.text = m.AccountLinkBannerText ?? string.Empty;
        _accountBanner.color = AndroidHistoryUi.StatusColor(m.AccountLinkBannerSeverity);
        AndroidHistoryUi.SetActive(_accountLinked.GameObject, m.AccountAlreadyLinkedButtonVisible);
        _accountLinked.Set(m.AccountAlreadyLinkedButtonText);
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
/// The favorite note editor on Android, the desktop's note dialog on the touch layout: the note
/// opens on the selected favorite and saves to the favorite it opened on, even if a reload
/// reselects another row meanwhile. A failed save keeps the draft; the status line says why. The
/// character count is polled each frame instead of a change listener.
/// </summary>
internal sealed class AndroidHistoryNoteDialog
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

    public AndroidHistoryNoteDialog(
        RectTransform parent,
        TMP_FontAsset font,
        HistoryTouchLayout layout,
        Func<string, string, Task<bool>> saveNote
    )
    {
        _saveNote = saveNote;
        var root = AndroidHistoryUi.Overlay(
            parent,
            "FavoriteNote",
            "note_outside",
            () => Close("outside_tap")
        );
        _root = root.gameObject;
        var dialog = AndroidHistoryUi.Panel(root, "Dialog", layout.Dialog);
        var origin = layout.Dialog;
        var title = AndroidHistoryUi.Label(
            dialog,
            font,
            "Title",
            Local(layout.DialogTitle, origin),
            layout.TitleFont
        );
        title.text = HistoryPanelText.FavoriteNoteTitle();
        AndroidHistoryUi.Button(
            dialog,
            font,
            "note_close",
            Local(layout.DialogClose, origin),
            layout.TitleFont,
            () => Close("close_button"),
            AndroidUiKit.CloseGlyph
        );
        _input = AndroidHistoryUi.Input(
            dialog,
            font,
            "Note",
            Local(layout.NoteInput, origin),
            layout.ButtonFont,
            HistoryPanelText.FavoriteNotePlaceholder(),
            RunLogSchema.FavoriteNoteMaxLength,
            multiLine: true
        );
        _count = AndroidHistoryUi.Label(
            dialog,
            font,
            "Count",
            Local(layout.NoteCount, origin),
            layout.BodyFont,
            color: AndroidHistoryUi.Muted
        );
        AndroidHistoryUi
            .Button(
                dialog,
                font,
                "note_cancel",
                Local(layout.NoteCancel, origin),
                layout.ButtonFont,
                () => Close("cancel_button")
            )
            .Set(HistoryPanelText.Cancel());
        _save = AndroidHistoryUi.Button(
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

    public void Open(string battleId, string note)
    {
        _battleId = battleId;
        _saving = false;
        _input.SetTextWithoutNotify(note);
        _shownLength = -1;
        Tick();
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.note_opened"),
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
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.note_closed"),
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

    /// <summary>Main thread, every frame while open: the count follows the draft.</summary>
    public void Tick()
    {
        var length = (_input.text ?? string.Empty).Length;
        if (length == _shownLength)
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
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.note_saved"),
            ("saved", saved),
            ("note_length", note.Length)
        );
        if (saved)
            Close("saved");
    }

    private static TouchRect Local(TouchRect rect, TouchRect origin) =>
        new(rect.Left - origin.Left, rect.Top - origin.Top, rect.Width, rect.Height);
}
