#nullable enable
using BazaarPlusPlus.Infrastructure.DesktopKit;
using BazaarPlusPlus.Infrastructure.UiTokens;
using BazaarPlusPlus.Storage.RunLog;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    private const int NoteLines = 7;

    private Action? _favorite;
    private Func<string, string, Task<bool>>? _saveNote;
    private KitButton _favoriteButton = null!,
        _noteSave = null!;
    private KitModal? _note;
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

    private void CreateFavoriteButton(DesktopKit kit, Transform actions) =>
        _favoriteButton = kit.Button(
            actions,
            string.Empty,
            KitButtonKind.Secondary,
            KitSize.Md,
            () =>
            {
                if (_model != null)
                    _favorite?.Invoke();
            }
        );

    private void CreateNoteDialog(DesktopKit kit)
    {
        _note = kit.Modal(_layout!, "HistoryFavoriteNoteDialog", DialogWidth, InGameTheme.Scrim);
        var card = _note.Card;
        kit.Text(card, HistoryPanelText.FavoriteNoteTitle(), KitTextRole.Title);
        var input = kit.Field(card, HistoryPanelText.FavoriteNotePlaceholder(), lines: NoteLines);
        input.characterLimit = RunLogSchema.FavoriteNoteMaxLength;
        input.onValueChanged.AddListener(_ => RefreshNoteCount());
        input.onSelect.AddListener(_ => UpdateInputFocus());
        input.onDeselect.AddListener(_ => ReleaseInputFocus());
        _noteInput = input;
        var foot = Line(card, "Foot", InGameSizes.Space8);
        _noteCount = Flexible(kit.Text(foot.transform, string.Empty, KitTextRole.Caption));
        kit.Button(
            foot.transform,
            HistoryPanelText.Cancel(),
            KitButtonKind.Secondary,
            KitSize.Md,
            CloseNoteDialog
        );
        _noteSave = kit.Button(
            foot.transform,
            HistoryPanelText.SaveNote(),
            KitButtonKind.Primary,
            KitSize.Md,
            () => _ = SaveNoteAsync()
        );
    }

    internal void OpenNoteDialog(string battleId, string note)
    {
        if (_note == null || _noteInput == null)
            return;
        _noteBattleId = battleId;
        _noteSaving = false;
        _noteInput.SetTextWithoutNotify(note);
        RefreshNoteCount();
        _note.Root.SetActive(true);
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
        _note?.Root.SetActive(false);
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

    private void RefreshFavoriteControls(bool hasBattle)
    {
        var m = _model!;
        _favoriteButton.gameObject.SetActive(m.FavoriteButtonVisible && hasBattle);
        _favoriteButton.Label = m.FavoriteButtonText;
        _favoriteButton.interactable = m.FavoriteButtonEnabled && !m.PageLoading;
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
