#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Infrastructure.DesktopKit;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    private const float DialogWidth = 600f;

    private readonly Action _record;
    private readonly Action _delete;
    private readonly Action _checkHealth;
    private readonly Action<string?> _submitAccountCode;
    private readonly Action _toggleAccountLink;
    private readonly Action _markAccountLinked;
    private Action? _toggleBppAccount;
    private Action? _openBppActivateDevice;
    private Action<string?>? _submitBppAccount;
    private string _accountCode = string.Empty;
    private string _bppAccountText = string.Empty;
    private BppAccountFlowPhase _bppAccountRenderedPhase;
    private string? _inputAccount;
    private TMP_InputField? _accountInput;
    private TMP_InputField? _bppAccountInput;
    private KitModal? _more;
    private GameObject _accountForm = null!;
    private GameObject _bppAccountForm = null!;
    private KitButton _deleteButton = null!,
        _recordButton = null!,
        _replayButton = null!,
        _healthButton = null!,
        _accountToggle = null!,
        _accountSubmit = null!,
        _accountLinked = null!,
        _bppAccountToggle = null!,
        _bppAccountActivate = null!,
        _bppAccountSubmit = null!;
    private TextMeshProUGUI _databaseLabel = null!,
        _moreStatus = null!,
        _accountTitle = null!,
        _accountStatus = null!,
        _accountPersistence = null!,
        _accountWhy = null!,
        _accountBanner = null!,
        _bppAccountTitle = null!,
        _bppAccountStatus = null!,
        _bppAccountHint = null!,
        _bppAccountPrompt = null!,
        _bppAccountNotice = null!,
        _bppAccountBanner = null!;

    private void CreateActions(DesktopKit kit, RectTransform detail)
    {
        var actions = Line(detail, "Actions", InGameSizes.Space8);
        DesktopKit.Size(actions, -1, InGameSizes.ControlMd);
        _deleteButton = kit.Button(
            actions.transform,
            string.Empty,
            KitButtonKind.Danger,
            KitSize.Md,
            _delete
        );
        _statusLabel = Flexible(kit.Text(actions.transform, string.Empty, KitTextRole.Caption));
        _statusLabel.textWrappingMode = TextWrappingModes.Normal;
        CreateFavoriteButton(kit, actions.transform);
        _recordButton = kit.Button(
            actions.transform,
            string.Empty,
            KitButtonKind.Secondary,
            KitSize.Md,
            _record
        );
        _replayButton = kit.Button(
            actions.transform,
            string.Empty,
            KitButtonKind.Primary,
            KitSize.Md,
            _replay
        );
    }

    private void CreateMoreDialog(DesktopKit kit)
    {
        _more = kit.Modal(_layout!, "HistoryMoreDialog", DialogWidth, InGameTheme.Scrim);
        var card = _more.Card;
        // The card grows down from a fixed top, so opening the link form moves nothing
        // that is already on show.
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, 1);
        card.anchoredPosition = new Vector2(0, -180);

        var head = Line(card, "Head", InGameSizes.Space8);
        Flexible(kit.Text(head.transform, HistoryPanelText.More(), KitTextRole.Title));
        kit.IconButton(
            head.transform,
            KitIcon.Close,
            KitButtonKind.Secondary,
            KitSize.Sm,
            () => SetMoreVisible(false)
        );

        var database = Line(card, "Database", InGameSizes.Space12);
        _databaseLabel = Flexible(kit.Text(database.transform, string.Empty, KitTextRole.Body));
        _healthButton = kit.Button(
            database.transform,
            string.Empty,
            KitButtonKind.Secondary,
            KitSize.Sm,
            _checkHealth
        );
        _moreStatus = Wrapped(kit.Text(card, string.Empty, KitTextRole.Meta));
        kit.Hairline(card, .7f);

        var account = Line(card, "Account", InGameSizes.Space12);
        account.childAlignment = TextAnchor.UpperLeft;
        var words = Flexible(DesktopKit.Rect(account.transform, "Words"));
        Column(words, InGameSizes.Space4);
        _accountTitle = kit.Text(words, string.Empty, KitTextRole.Section);
        _accountStatus = Wrapped(kit.Text(words, string.Empty, KitTextRole.Body));
        _accountPersistence = Wrapped(kit.Text(words, string.Empty, KitTextRole.Caption));
        var buttons = DesktopKit.Rect(account.transform, "Buttons");
        Column(buttons, InGameSizes.Space8);
        _accountToggle = kit.Button(
            buttons,
            string.Empty,
            KitButtonKind.Secondary,
            KitSize.Sm,
            _toggleAccountLink
        );
        _accountLinked = kit.Button(
            buttons,
            string.Empty,
            KitButtonKind.Quiet,
            KitSize.Sm,
            _markAccountLinked
        );

        var form = DesktopKit.Rect(card, "AccountForm");
        Column(form, InGameSizes.Space8);
        _accountWhy = Wrapped(kit.Text(form, string.Empty, KitTextRole.Meta));
        var code = Line(form, "Code", InGameSizes.Space8);
        var input = Flexible(kit.Field(code.transform, HistoryPanelText.AccountLink.EmptyCode()));
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
        _accountSubmit = kit.Button(
            code.transform,
            string.Empty,
            KitButtonKind.Primary,
            KitSize.Md,
            () => _submitAccountCode(_accountCode.Trim())
        );
        _accountBanner = Wrapped(kit.Text(form, string.Empty, KitTextRole.Meta));
        _accountForm = form.gameObject;

        kit.Hairline(card, .7f);

        var bppRow = Line(card, "BppAccount", InGameSizes.Space12);
        bppRow.childAlignment = TextAnchor.UpperLeft;
        var bppWords = Flexible(DesktopKit.Rect(bppRow.transform, "Words"));
        Column(bppWords, InGameSizes.Space4);
        _bppAccountTitle = kit.Text(bppWords, string.Empty, KitTextRole.Section);
        _bppAccountStatus = Wrapped(kit.Text(bppWords, string.Empty, KitTextRole.Body));
        _bppAccountHint = Wrapped(kit.Text(bppWords, string.Empty, KitTextRole.Caption));
        var bppButtons = DesktopKit.Rect(bppRow.transform, "Buttons");
        Column(bppButtons, InGameSizes.Space8);
        _bppAccountToggle = kit.Button(
            bppButtons,
            string.Empty,
            KitButtonKind.Secondary,
            KitSize.Sm,
            () => _toggleBppAccount?.Invoke()
        );
        _bppAccountActivate = kit.Button(
            bppButtons,
            string.Empty,
            KitButtonKind.Quiet,
            KitSize.Sm,
            () => _openBppActivateDevice?.Invoke()
        );

        var bppForm = DesktopKit.Rect(card, "BppAccountForm");
        Column(bppForm, InGameSizes.Space8);
        _bppAccountPrompt = Wrapped(kit.Text(bppForm, string.Empty, KitTextRole.Body));
        _bppAccountNotice = Wrapped(kit.Text(bppForm, string.Empty, KitTextRole.Caption));
        var bppLine = Line(bppForm, "InputLine", InGameSizes.Space8);
        var bppInput = Flexible(
            kit.Field(bppLine.transform, HistoryPanelText.BppAccount.EmailPlaceholder())
        );
        bppInput.characterLimit = 254;
        bppInput.onValueChanged.AddListener(value => _bppAccountText = value);
        bppInput.onSelect.AddListener(_ => UpdateInputFocus());
        bppInput.onDeselect.AddListener(_ => ReleaseInputFocus());
        bppInput.onSubmit.AddListener(_ =>
        {
            if (_model?.BppAccountControlsEnabled == true)
                _submitBppAccount?.Invoke(_bppAccountText.Trim());
        });
        _bppAccountInput = bppInput;
        _bppAccountSubmit = kit.Button(
            bppLine.transform,
            string.Empty,
            KitButtonKind.Primary,
            KitSize.Md,
            () => _submitBppAccount?.Invoke(_bppAccountText.Trim())
        );
        _bppAccountBanner = Wrapped(kit.Text(card, string.Empty, KitTextRole.Meta));
        _bppAccountForm = bppForm.gameObject;
    }

    private void SetMoreVisible(bool visible)
    {
        if (!visible)
        {
            _accountInput?.DeactivateInputField();
            _bppAccountInput?.DeactivateInputField();
            ReleaseInputFocus();
        }
        _more?.Root.SetActive(visible);
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
            _bppAccountText = string.Empty;
            _bppAccountInput?.SetTextWithoutNotify(string.Empty);
            _bppAccountInput?.DeactivateInputField();
            ReleaseInputFocus();
        }
        if (_bppAccountRenderedPhase != m.BppAccountPhase)
        {
            _bppAccountRenderedPhase = m.BppAccountPhase;
            _bppAccountText = string.Empty;
            _bppAccountInput?.SetTextWithoutNotify(string.Empty);
        }
        var hasRows =
            m.SectionMode == HistorySectionMode.Runs
                ? m.Runs.Count > 0
                : m.VisibleBattles.Count > 0;
        var hasBattle = hasRows && m.VisibleBattles.Count > 0;
        _deleteButton.gameObject.SetActive(m.DeleteButtonVisible && hasRows);
        _deleteButton.Label = m.DeleteButtonText;
        _deleteButton.interactable = m.DeleteButtonEnabled && !m.PageLoading;
        _recordButton.gameObject.SetActive(m.RecordAndReplayButtonVisible && hasBattle);
        _recordButton.Label = m.RecordAndReplayButtonText;
        _recordButton.interactable = m.RecordAndReplayButtonEnabled && !m.PageLoading;
        // Keep unavailable actions pressable so a click can show the reason in the status line.
        _replayButton.gameObject.SetActive(hasBattle);
        _replayButton.Label = m.ReplayButtonText;
        _replayButton.interactable = !m.ReplayButtonBusy && !m.PageLoading;
        _replayButton.Dimmed = !m.ReplayButtonEnabled;
        RefreshFavoriteControls(hasBattle);

        _databaseLabel.text = m.DatabaseChipText;
        _databaseLabel.color = StatusColor(m.DatabaseChipSeverity);
        _healthButton.Label = m.ServerHealthButtonText;
        _healthButton.interactable = m.ServerHealthButtonEnabled;
        Show(_moreStatus, m.StatusMessage, StatusColor(m.StatusSeverity));
        _accountTitle.text = m.AccountTitleText;
        _accountStatus.text = m.AccountRowStatusText;
        Show(_accountPersistence, m.AccountPersistenceText, Muted);
        _accountToggle.gameObject.SetActive(m.AccountRowActionVisible);
        _accountToggle.Label = m.AccountLinkFormVisible
            ? m.AccountLinkCollapseText
            : m.AccountRowActionText;
        _accountToggle.interactable = m.AccountLinkInputEnabled;
        _accountLinked.gameObject.SetActive(m.AccountAlreadyLinkedButtonVisible);
        _accountLinked.Label = m.AccountAlreadyLinkedButtonText;
        if (!m.AccountLinkFormVisible)
        {
            _accountInput?.DeactivateInputField();
            ReleaseInputFocus();
        }
        _accountForm.SetActive(m.AccountLinkFormVisible);
        Show(_accountWhy, m.AccountHintText, Muted);
        _accountInput!.interactable = m.AccountLinkInputEnabled;
        _accountSubmit.Label = m.AccountLinkButtonText;
        _accountSubmit.interactable = m.AccountLinkButtonEnabled;
        Show(_accountBanner, m.AccountLinkBannerText, StatusColor(m.AccountLinkBannerSeverity));

        _bppAccountTitle.text = m.BppAccountTitleText;
        _bppAccountStatus.text = m.BppAccountStatusText;
        Show(_bppAccountHint, m.BppAccountHintText, Muted);
        _bppAccountToggle.gameObject.SetActive(m.BppAccountActionVisible);
        _bppAccountToggle.Label = m.BppAccountActionText;
        _bppAccountToggle.interactable = m.BppAccountControlsEnabled;
        _bppAccountActivate.gameObject.SetActive(m.BppAccountActivateActionVisible);
        _bppAccountActivate.Label = m.BppAccountActivateActionText;
        _bppAccountActivate.interactable = m.BppAccountControlsEnabled;
        if (!m.BppAccountFormVisible || !m.BppAccountInputVisible)
        {
            _bppAccountInput?.DeactivateInputField();
            if (!m.AccountLinkFormVisible)
                ReleaseInputFocus();
        }
        _bppAccountForm.SetActive(m.BppAccountFormVisible);
        Show(_bppAccountPrompt, m.BppAccountFormPromptText, Ink);
        Show(_bppAccountNotice, m.BppAccountNoticeText, Muted);
        if (_bppAccountInput != null)
        {
            _bppAccountInput.gameObject.SetActive(m.BppAccountInputVisible);
            _bppAccountInput.characterLimit = m.BppAccountInputMaxLength;
            _bppAccountInput.interactable = m.BppAccountControlsEnabled;
            if (_bppAccountInput.placeholder is TextMeshProUGUI placeholder)
                placeholder.text = m.BppAccountInputPlaceholder;
        }
        _bppAccountSubmit.Label = m.BppAccountSubmitText;
        _bppAccountSubmit.interactable = m.BppAccountControlsEnabled;
        Show(_bppAccountBanner, m.BppAccountBannerText, StatusColor(m.BppAccountBannerSeverity));
    }

    private static void Show(TextMeshProUGUI text, string? value, Color color)
    {
        text.text = value ?? string.Empty;
        text.color = color;
        var shown = text.text.Length > 0;
        if (text.gameObject.activeSelf != shown)
            text.gameObject.SetActive(shown);
    }

    private static Color StatusColor(StatusSeverity severity) =>
        severity switch
        {
            StatusSeverity.Failure => InGameTheme.Failure,
            StatusSeverity.Success => InGameTheme.Success,
            StatusSeverity.Confirm => Ink,
            _ => Muted,
        };
}
