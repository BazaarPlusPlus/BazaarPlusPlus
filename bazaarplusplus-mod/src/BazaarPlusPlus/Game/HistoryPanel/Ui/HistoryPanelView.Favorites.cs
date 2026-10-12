#nullable enable
using BazaarPlusPlus.Infrastructure.UiTokens;
using BazaarPlusPlus.Storage.RunLog;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    private Action? _favorite;
    private Func<string, string, Task<bool>>? _saveNote;
    private Button _favoriteButton = null!,
        _noteSave = null!;
    private GameObject? _noteRoot;
    private TMP_InputField? _noteInput;
    private TextMeshProUGUI _noteCount = null!;

    // The favorite owns the note the dialog edits, so a reload that reselects another row while
    // the dialog is open still saves to the favorite it opened on.
    private string? _noteBattleId;
    private bool _noteSaving;

    internal void SetFavoriteActions(Action favorite, Func<string, string, Task<bool>> saveNote)
    {
        _favorite = favorite;
        _saveNote = saveNote;
    }

    private void CreateFavoriteControls()
    {
        _favoriteButton = Button(_layout!, "", .38f, .90f, .19f, .055f, OnFavoriteButton);
        var shade = CreateRect("HistoryFavoriteNoteDialog", _layout!, 0, 0, 1, 1);
        var canvas = shade.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = BppOverlaySorting.PanelForeground;
        shade.gameObject.AddComponent<GraphicRaycaster>();
        shade.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .75f);
        var dialog = CreateRect("Dialog", shade, .26f, .24f, .48f, .48f);
        dialog.gameObject.AddComponent<Image>().color = new Color(.065f, .043f, .025f);
        Panel(dialog, 0, 0, 1, 1);
        Text(dialog, HistoryPanelText.FavoriteNoteTitle(), .05f, .04f, .6f, .09f, 25);
        var inputRect = CreateRect("FavoriteNote", dialog, .05f, .17f, .90f, .57f);
        var background = inputRect.gameObject.AddComponent<Image>();
        background.color = new Color(.20f, .15f, .09f);
        var viewport = CreateRect("TextViewport", inputRect, .025f, .03f, .95f, .94f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var inputText = Wrapped(Text(viewport, "", 0, 0, 1, 1, 17));
        inputText.alignment = TextAlignmentOptions.TopLeft;
        inputText.overflowMode = TextOverflowModes.Overflow;
        var placeholder = Wrapped(
            Text(viewport, HistoryPanelText.FavoriteNotePlaceholder(), 0, 0, 1, 1, 15, Muted)
        );
        placeholder.alignment = TextAlignmentOptions.TopLeft;
        var input = inputRect.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = viewport;
        input.textComponent = inputText;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.MultiLineNewline;
        input.characterLimit = RunLogSchema.FavoriteNoteMaxLength;
        input.onValueChanged.AddListener(_ => RefreshNoteCount());
        input.onSelect.AddListener(_ => UpdateInputFocus());
        input.onDeselect.AddListener(_ => ReleaseInputFocus());
        _noteInput = input;
        _noteCount = Text(dialog, "", .05f, .78f, .30f, .07f, 13, Muted);
        Button(dialog, HistoryPanelText.Cancel(), .50f, .78f, .20f, .12f, CloseNoteDialog);
        _noteSave = Button(
            dialog,
            HistoryPanelText.SaveNote(),
            .73f,
            .78f,
            .22f,
            .12f,
            () => _ = SaveNoteAsync(),
            true
        );
        _noteRoot = shade.gameObject;
        _noteRoot.SetActive(false);
    }

    private void OnFavoriteButton()
    {
        if (_model == null)
            return;
        if (_model.SectionMode == HistorySectionMode.Favorites)
            OpenNoteDialog();
        else
            _favorite?.Invoke();
    }

    private void OpenNoteDialog()
    {
        if (_noteRoot == null || _noteInput == null || _model?.SelectedFavoriteBattleId == null)
            return;
        _noteBattleId = _model.SelectedFavoriteBattleId;
        _noteSaving = false;
        _noteInput.SetTextWithoutNotify(_model.SelectedFavoriteNote);
        RefreshNoteCount();
        _noteRoot.SetActive(true);
        _noteInput.ActivateInputField();
    }

    private void CloseNoteDialog()
    {
        _noteBattleId = null;
        _noteSaving = false;
        if (_noteInput != null)
        {
            _noteInput.DeactivateInputField();
            _noteInput.SetTextWithoutNotify(string.Empty);
        }
        ReleaseInputFocus();
        _noteRoot?.SetActive(false);
    }

    // A failed save keeps the dialog and the draft; the status line says why.
    private async Task SaveNoteAsync()
    {
        if (_noteSaving || _noteBattleId == null || _noteInput == null || _saveNote == null)
            return;
        var battleId = _noteBattleId;
        _noteSaving = true;
        RefreshNoteCount();
        bool saved;
        try
        {
            saved = await _saveNote(battleId, _noteInput.text);
        }
        catch (Exception)
        {
            saved = false;
        }
        if (_disposed || _noteBattleId != battleId)
            return;
        _noteSaving = false;
        if (saved)
            CloseNoteDialog();
        else
            RefreshNoteCount();
    }

    private void RefreshNoteCount()
    {
        if (_noteInput == null)
            return;
        _noteCount.text = HistoryPanelText.FavoriteNoteCount(
            _noteInput.text.Length,
            RunLogSchema.FavoriteNoteMaxLength
        );
        _noteSave.interactable = !_noteSaving;
    }

    private void RefreshFavoriteControls()
    {
        var m = _model!;
        _favoriteButton.gameObject.SetActive(m.FavoriteButtonVisible);
        SetButton(
            _favoriteButton,
            m.FavoriteButtonText,
            enabled: m.FavoriteButtonEnabled && !m.PageLoading
        );
        // The favorite the dialog edits is gone (removed, or the section changed): drop the draft.
        if (
            _noteBattleId != null
            && !_noteSaving
            && (
                m.SectionMode != HistorySectionMode.Favorites
                || !m.VisibleBattles.Any(battle => battle.BattleId == _noteBattleId)
            )
            && !m.PageLoading
        )
            CloseNoteDialog();
    }
}
