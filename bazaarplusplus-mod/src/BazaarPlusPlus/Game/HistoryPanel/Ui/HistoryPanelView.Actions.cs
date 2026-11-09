#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.GameInterop.NativeSkin;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    private readonly Action _record;
    private readonly Action _delete;
    private readonly Action _checkHealth;
    private readonly Action<string?> _submitAccountCode;
    private readonly Action _toggleAccountLink;
    private readonly Action _markAccountLinked;
    private bool _moreVisible;
    private float _morePlacementHeight;
    private string _accountCode = string.Empty;
    private string? _inputAccount;
    private TMP_InputField? _accountInput;

    private GameObject? _moreRoot;
    private RectTransform _moreDialog = null!,
        _accountForm = null!;
    private Button _deleteButton = null!,
        _recordButton = null!,
        _replayButton = null!,
        _healthButton = null!,
        _moreClose = null!,
        _accountToggle = null!,
        _accountSubmit = null!,
        _accountLinked = null!;
    private TextMeshProUGUI _databaseLabel = null!,
        _moreTitle = null!,
        _moreStatus = null!,
        _accountTitle = null!,
        _accountStatus = null!,
        _accountPersistence = null!,
        _accountWhy = null!,
        _accountBanner = null!;

    private void CreateActions()
    {
        Button(
            _layout!,
            HistoryPanelText.More(),
            HistoryPanelLayout.MoreButton,
            () => SetMoreVisible(!_moreVisible),
            style: ButtonStyle.Standard
        );
        _deleteButton = Button(
            _layout!,
            "",
            HistoryPanelLayout.DeleteButton,
            _delete,
            style: ButtonStyle.Danger
        );
        _recordButton = Button(_layout!, "", HistoryPanelLayout.RecordButton, _record);
        _replayButton = Button(
            _layout!,
            "",
            HistoryPanelLayout.ReplayButton,
            _replay,
            true,
            23,
            ButtonStyle.Primary
        );
        CreateFavoriteControls();
        var shade = CreateRect("HistoryMoreDialog", _layout!, 0, 0, 1, 1);
        var canvas = shade.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = BppOverlaySorting.PanelForeground;
        shade.gameObject.AddComponent<GraphicRaycaster>();
        shade.gameObject.AddComponent<Image>().color = InGameTheme.Scrim;
        var dialog = CreateRect("Dialog", shade, 0, 0, 1, 1);
        _moreDialog = dialog;
        var frame = CreateRect("DialogFrame", dialog, 0, 0, 1, 1);
        frame.gameObject.AddComponent<Image>().color = InGameTheme.Dialog;
        Panel(frame, 0, 0, 1, 1);
        _moreTitle = Text(dialog, HistoryPanelText.More(), 0, 0, 1, 1, 26);
        _moreClose = Button(
            dialog,
            HistoryPanelText.Close(),
            .81f,
            .045f,
            .14f,
            .06f,
            () => SetMoreVisible(false),
            size: 16
        );
        _databaseLabel = Text(dialog, "", 0, 0, 1, 1, 16);
        _healthButton = Button(dialog, "", 0, 0, 1, 1, _checkHealth, size: 16);
        _moreStatus = Wrapped(Text(dialog, "", 0, 0, 1, 1, 14));
        _accountTitle = Text(dialog, "", 0, 0, 1, 1, 20);
        _accountTitle.overflowMode = TextOverflowModes.Overflow;
        _accountStatus = Wrapped(Text(dialog, "", 0, 0, 1, 1, 18, Ink));
        _accountPersistence = Wrapped(Text(dialog, "", 0, 0, 1, 1, 13, Muted));
        _accountPersistence.overflowMode = TextOverflowModes.Overflow;
        _accountToggle = Button(dialog, "", .76f, .094f, .24f, .086f, _toggleAccountLink, size: 16);
        _accountForm = CreateRect("AccountForm", dialog, 0, 0, 1, 1);
        _accountWhy = Wrapped(Text(_accountForm, "", 0, 0, 1, 1, 14, Muted));
        var inputRect = CreateRect("AccountLinkCode", _accountForm, 0, 0, 1, 1);
        var background = inputRect.gameObject.AddComponent<Image>();
        background.sprite = DesktopNativeSkin.HeaderNormal;
        background.type = Image.Type.Sliced;
        background.pixelsPerUnitMultiplier = DesktopNativeSkin.HeaderPixelsPerUnit;
        background.color = InGameTheme.ArtInputTint;
        var viewport = CreateRect("TextViewport", inputRect, .035f, .03f, .93f, .94f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var inputText = Text(viewport, "", 0, 0, 1, 1, 20);
        var placeholder = Text(
            viewport,
            HistoryPanelText.AccountLink.EmptyCode(),
            0,
            0,
            1,
            1,
            16,
            Muted
        );
        var input = inputRect.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = viewport;
        input.textComponent = inputText;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 10;
        input.onValidateInput = (_, _, character) =>
            char.IsWhiteSpace(character) ? '\0' : character;
        input.onValueChanged.AddListener(value => _accountCode = value);
        input.onSelect.AddListener(_ => UpdateInputFocus());
        input.onDeselect.AddListener(_ => ReleaseInputFocus());
        input.onSubmit.AddListener(_ =>
        {
            if (_model?.AccountLinkButtonEnabled == true)
                _submitAccountCode(_accountCode);
        });
        _accountInput = input;
        _accountSubmit = Button(
            _accountForm,
            "",
            .65f,
            .25f,
            .35f,
            .14f,
            () => _submitAccountCode(_accountCode.Trim()),
            true,
            16,
            ButtonStyle.Primary
        );
        _accountBanner = Wrapped(Text(_accountForm, "", 0, 0, 1, 1, 14));
        _accountLinked = Button(dialog, "", .65f, .65f, .35f, .11f, _markAccountLinked, size: 16);
        _moreRoot = shade.gameObject;
        _moreRoot.SetActive(false);
    }

    private void SetMoreVisible(bool visible)
    {
        if (visible && !_moreVisible)
        {
            _morePlacementHeight = 0;
            LayoutMoreDialog();
        }
        _moreVisible = visible;
        if (!visible)
        {
            _accountInput?.DeactivateInputField();
            ReleaseInputFocus();
        }
        _moreRoot?.SetActive(visible);
    }

    private void RefreshActions()
    {
        var m = _model!;
        if (_inputAccount != m.AccountId)
        {
            _inputAccount = m.AccountId;
            _accountCode = string.Empty;
            _accountInput?.SetTextWithoutNotify(string.Empty);
            _accountInput?.DeactivateInputField();
            ReleaseInputFocus();
        }
        _deleteButton.gameObject.SetActive(m.DeleteButtonVisible);
        SetButton(
            _deleteButton,
            m.DeleteButtonText,
            enabled: m.DeleteButtonEnabled && !m.PageLoading
        );
        _recordButton.gameObject.SetActive(m.RecordAndReplayButtonVisible);
        SetButton(
            _recordButton,
            m.RecordAndReplayButtonText,
            enabled: m.RecordAndReplayButtonEnabled && !m.PageLoading
        );
        // Unavailable reads as dimmed but stays pressable: the press puts the reason on the status
        // line. A busy replay or a loading page does not take presses.
        SetButton(
            _replayButton,
            m.ReplayButtonText,
            true,
            !m.ReplayButtonBusy && !m.PageLoading,
            dimmed: !m.ReplayButtonEnabled
        );
        RefreshFavoriteControls();
        var hasRows =
            m.SectionMode == HistorySectionMode.Runs
                ? m.Runs.Count > 0
                : m.VisibleBattles.Count > 0;
        var hasBattle = hasRows && m.VisibleBattles.Count > 0;
        _deleteButton.gameObject.SetActive(m.DeleteButtonVisible && hasRows);
        _favoriteButton.gameObject.SetActive(m.FavoriteButtonVisible && hasBattle);
        _recordButton.gameObject.SetActive(m.RecordAndReplayButtonVisible && hasBattle);
        _replayButton.gameObject.SetActive(hasBattle);
        _databaseLabel.text = m.DatabaseChipText;
        _databaseLabel.color = StatusColor(m.DatabaseChipSeverity);
        SetButton(_healthButton, m.ServerHealthButtonText, enabled: m.ServerHealthButtonEnabled);
        _moreStatus.text = m.StatusMessage ?? "";
        _moreStatus.color = StatusColor(m.StatusSeverity);
        _accountTitle.text = m.AccountTitleText;
        _accountStatus.text = m.AccountRowStatusText;
        _accountPersistence.text = m.AccountPersistenceText;
        if (
            m.AccountPersistenceText.Length > 0
            && !m.AccountPersistenceText.Contains('\n')
            && _accountStatus.GetPreferredValues(m.AccountRowStatusText, 10000, 0).x
                + 12
                + _accountPersistence.GetPreferredValues(m.AccountPersistenceText, 10000, 0).x
                <= HistoryMoreLayout.LabelWidth
        )
        {
            _accountStatus.text =
                m.AccountRowStatusText
                + "<space=12><size=13><color=#"
                + ColorUtility.ToHtmlStringRGBA(Muted)
                + ">"
                + m.AccountPersistenceText
                + "</color></size>";
            _accountPersistence.text = string.Empty;
        }
        _accountToggle.gameObject.SetActive(m.AccountRowActionVisible);
        SetButton(
            _accountToggle,
            m.AccountLinkFormVisible ? m.AccountLinkCollapseText : m.AccountRowActionText,
            enabled: m.AccountLinkInputEnabled
        );
        if (!m.AccountLinkFormVisible)
        {
            _accountInput?.DeactivateInputField();
            ReleaseInputFocus();
        }
        _accountForm.gameObject.SetActive(m.AccountLinkFormVisible);
        _accountWhy.text = m.AccountHintText;
        _accountInput!.interactable = m.AccountLinkInputEnabled;
        SetButton(_accountSubmit, m.AccountLinkButtonText, true, m.AccountLinkButtonEnabled);
        _accountBanner.text = m.AccountLinkBannerText ?? "";
        _accountBanner.color = StatusColor(m.AccountLinkBannerSeverity);
        _accountLinked.gameObject.SetActive(m.AccountAlreadyLinkedButtonVisible);
        SetButton(_accountLinked, m.AccountAlreadyLinkedButtonText);
        LayoutMoreDialog();
    }

    private void LayoutMoreDialog()
    {
        if (_model is not { } m || _moreDialog == null || _layout == null)
            return;
        var labelWidth = m.AccountRowActionVisible
            ? HistoryMoreLayout.LabelWidth
            : HistoryMoreLayout.ContentWidth;
        HistoryMoreLayout Measure(bool formVisible, float minimumBanner = 0) =>
            new(
                MoreTextHeight(_moreStatus),
                MoreTextHeight(_accountStatus, labelWidth),
                MoreTextHeight(_accountPersistence, labelWidth),
                m.AccountRowActionVisible,
                formVisible,
                MoreTextHeight(_accountWhy),
                Mathf.Max(minimumBanner, MoreTextHeight(_accountBanner)),
                m.AccountAlreadyLinkedButtonVisible
            );
        var layout = Measure(m.AccountLinkFormVisible);
        var expanded = m.AccountRowActionVisible ? Measure(true, 48) : layout;
        _morePlacementHeight = Mathf.Max(_morePlacementHeight, expanded.Height);
        _moreDialog.anchorMin = _moreDialog.anchorMax = new Vector2(.5f, .5f);
        _moreDialog.pivot = new Vector2(.5f, 1);
        _moreDialog.sizeDelta = new Vector2(HistoryMoreLayout.Width, layout.Height);
        var scale = layout.FitScale(
            _layout.rect.width - 48,
            _layout.rect.height - 48,
            _morePlacementHeight
        );
        _moreDialog.anchoredPosition = new Vector2(0, _morePlacementHeight * scale / 2);
        _moreDialog.localScale = new Vector3(scale, scale, 1);
        Place(_moreTitle, layout.Title);
        Place(_moreClose, layout.Close);
        Place(_databaseLabel, layout.Database);
        Place(_healthButton, layout.Health);
        Place(_moreStatus, layout.Status);
        Place(_accountTitle, layout.AccountTitle);
        Place(_accountStatus, layout.AccountStatus);
        Place(_accountPersistence, layout.Guidance);
        Place(_accountToggle, layout.Toggle);
        Place(_accountLinked, layout.AlreadyLinked);
        Place(_accountWhy, layout.Hint);
        Place(_accountInput!, layout.Input);
        Place(_accountSubmit, layout.Submit);
        Place(_accountBanner, layout.Banner);
        _moreStatus.gameObject.SetActive(_moreStatus.text.Length > 0);
        _accountBanner.gameObject.SetActive(_accountBanner.text.Length > 0);
        _accountPersistence.gameObject.SetActive(_accountPersistence.text.Length > 0);

        static void Place(Component component, HistoryRect area) =>
            Anchor((RectTransform)component.transform, area.Anchors);
    }

    private static float MoreTextHeight(
        TextMeshProUGUI text,
        float width = HistoryMoreLayout.ContentWidth
    ) =>
        string.IsNullOrEmpty(text.text)
            ? 0
            : Mathf.Max(24, Mathf.Ceil(text.GetPreferredValues(width, 0).y) + 4);

    private static Color StatusColor(StatusSeverity severity) =>
        severity switch
        {
            StatusSeverity.Failure => InGameTheme.Failure,
            StatusSeverity.Success => InGameTheme.Success,
            StatusSeverity.Confirm => Ink,
            _ => Muted,
        };
}
