#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Infrastructure.UiTokens;
using TMPro;
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
    private string _accountCode = string.Empty;
    private string? _inputAccount;
    private TMP_InputField? _accountInput;

    private GameObject? _moreRoot;
    private RectTransform _accountCard = null!,
        _accountForm = null!;
    private Button _deleteButton = null!,
        _recordButton = null!,
        _replayButton = null!,
        _healthButton = null!,
        _accountToggle = null!,
        _accountSubmit = null!,
        _accountLinked = null!;
    private TextMeshProUGUI _databaseLabel = null!,
        _moreStatus = null!,
        _accountTitle = null!,
        _accountStatus = null!,
        _accountWhy = null!,
        _accountBanner = null!;

    private void CreateActions()
    {
        Button(
            _layout!,
            T("More", "更多"),
            .76f,
            .04f,
            .09f,
            .045f,
            () => SetMoreVisible(!_moreVisible)
        );
        _deleteButton = Button(_layout!, "", .04f, .90f, .16f, .044f, _delete);
        _recordButton = Button(_layout!, "", .58f, .90f, .13f, .055f, _record);
        _replayButton = Button(_layout!, "", .72f, .90f, .23f, .055f, _replay, true, 23);
        var shade = CreateRect("HistoryMoreDialog", _layout!, 0, 0, 1, 1);
        var canvas = shade.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = BppOverlaySorting.PanelForeground;
        shade.gameObject.AddComponent<GraphicRaycaster>();
        shade.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .75f);
        var dialog = CreateRect("Dialog", shade, .24f, .18f, .52f, .66f);
        dialog.gameObject.AddComponent<Image>().color = new Color(.065f, .043f, .025f);
        Panel(dialog, 0, 0, 1, 1);
        Text(dialog, T("History options", "历史记录选项"), .05f, .035f, .65f, .07f, 27);
        Button(
            dialog,
            HistoryPanelText.Close(),
            .81f,
            .045f,
            .14f,
            .06f,
            () => SetMoreVisible(false)
        );
        _databaseLabel = Text(dialog, "", .05f, .14f, .53f, .055f, 17);
        _healthButton = Button(dialog, "", .65f, .14f, .30f, .055f, _checkHealth);
        _moreStatus = Text(dialog, "", .05f, .21f, .90f, .075f, 14);
        _moreStatus.textWrappingMode = TextWrappingModes.Normal;
        _accountCard = CreateRect("AccountCard", dialog, .05f, .31f, .90f, .64f);
        _accountTitle = Text(_accountCard, "", 0, 0, .7f, .094f, 21);
        _accountStatus = Text(_accountCard, "", 0, .11f, .68f, .07f, 16, Muted);
        _accountToggle = Button(
            _accountCard,
            "",
            .76f,
            .094f,
            .24f,
            .086f,
            _toggleAccountLink,
            size: 15
        );
        _accountForm = CreateRect("AccountForm", _accountCard, 0, .22f, 1, .76f);
        _accountWhy = Text(_accountForm, "", 0, 0, 1, .2f, 14, Muted);
        _accountWhy.textWrappingMode = TextWrappingModes.Normal;
        var inputRect = CreateRect("AccountLinkCode", _accountForm, 0, .25f, .61f, .14f);
        var background = inputRect.gameObject.AddComponent<Image>();
        background.color = new Color(.20f, .15f, .09f);
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
            17
        );
        _accountBanner = Text(_accountForm, "", 0, .42f, 1, .18f, 14);
        _accountBanner.textWrappingMode = TextWrappingModes.Normal;
        _accountLinked = Button(
            _accountForm,
            "",
            .65f,
            .65f,
            .35f,
            .11f,
            _markAccountLinked,
            size: 15
        );
        _moreRoot = shade.gameObject;
        _moreRoot.SetActive(false);
    }

    private void SetMoreVisible(bool visible)
    {
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
        _deleteButton.gameObject.SetActive(ShowsBothBoards);
        SetButton(
            _deleteButton,
            m.DeleteButtonText,
            enabled: m.DeleteButtonEnabled && !m.PageLoading
        );
        SetButton(
            _recordButton,
            m.RecordAndReplayButtonText,
            enabled: m.RecordAndReplayButtonEnabled && !m.PageLoading
        );
        SetButton(_replayButton, m.ReplayButtonText, true, m.ReplayButtonEnabled && !m.PageLoading);
        _databaseLabel.text = m.DatabaseChipText;
        _databaseLabel.color = StatusColor(m.DatabaseChipSeverity);
        SetButton(_healthButton, m.ServerHealthButtonText, enabled: m.ServerHealthButtonEnabled);
        _moreStatus.text = m.StatusMessage ?? "";
        _moreStatus.color = StatusColor(m.StatusSeverity);
        _accountCard.gameObject.SetActive(m.AccountCardVisible);
        _accountTitle.text = m.AccountTitleText;
        _accountStatus.text = m.AccountRowStatusText;
        _accountToggle.gameObject.SetActive(m.AccountRowActionVisible);
        SetButton(
            _accountToggle,
            m.AccountLinkFormVisible ? m.AccountLinkCollapseText : m.AccountRowActionText
        );
        if (!m.AccountLinkFormVisible)
        {
            _accountInput?.DeactivateInputField();
            ReleaseInputFocus();
        }
        _accountForm.gameObject.SetActive(m.AccountLinkFormVisible);
        _accountWhy.text = m.AccountWhyText + "\n" + m.AccountHintText;
        _accountInput!.interactable = m.AccountLinkInputEnabled;
        SetButton(_accountSubmit, m.AccountLinkButtonText, true, m.AccountLinkButtonEnabled);
        _accountBanner.text = m.AccountLinkBannerText ?? "";
        _accountBanner.color = StatusColor(m.AccountLinkBannerSeverity);
        _accountLinked.gameObject.SetActive(m.AccountAlreadyLinkedButtonVisible);
        SetButton(_accountLinked, m.AccountAlreadyLinkedButtonText);
    }

    private static Color StatusColor(StatusSeverity severity) =>
        severity switch
        {
            StatusSeverity.Failure => new Color(.90f, .40f, .31f),
            StatusSeverity.Success => new Color(.48f, .76f, .42f),
            StatusSeverity.Confirm => Ink,
            _ => Muted,
        };
}
