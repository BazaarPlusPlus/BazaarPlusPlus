#nullable enable
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.GameInterop.Heroes;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelUiToolkitView
{
    private void BuildTree(VisualElement root)
    {
        var panel = new VisualElement();
        panel.style.flexGrow = 1f;
        panel.style.backgroundColor = Colors.HistoryPanelBackground;
        panel.style.paddingLeft = UiSpacing.PanelPadding;
        panel.style.paddingRight = UiSpacing.PanelPadding;
        panel.style.paddingTop = UiSpacing.PanelPadding;
        panel.style.paddingBottom = UiSpacing.Xxl;
        panel.style.flexDirection = FlexDirection.Row;
        root.Add(panel);

        BuildCoreArea(panel);
        BuildOperationRail(panel);
    }

    private void BuildCoreArea(VisualElement parent)
    {
        var core = new VisualElement();
        core.style.flexGrow = 1f;
        core.style.flexShrink = 1f;
        core.style.minWidth = 0f;
        core.style.minHeight = 0f;
        core.style.flexDirection = FlexDirection.Column;
        parent.Add(core);

        var selectorRow = new VisualElement();
        selectorRow.style.flexDirection = FlexDirection.Row;
        selectorRow.style.flexGrow = 1f;
        selectorRow.style.flexShrink = 1f;
        selectorRow.style.maxHeight = Length.Percent(Sizes.HistorySelectorRowHeightPercent);
        selectorRow.style.minHeight = Sizes.HistorySelectorRowMinHeight;
        selectorRow.style.minWidth = 0f;
        core.Add(selectorRow);

        BuildRunsSection(selectorRow);
        BuildBattlesSection(selectorRow);
        BuildPreview(core);
    }

    private void BuildRunsSection(VisualElement parent)
    {
        _runsSection = CreateSectionPanel(null);
        _runsSection.style.width = Length.Percent(Sizes.RunsColumnWidthPercent);
        _runsSection.style.flexGrow = 0f;
        _runsSection.style.flexShrink = 0f;
        _runsSection.style.minHeight = 0f;
        parent.Add(_runsSection);
        _runsSection.Add(CreateSectionTitle(HistoryPanelText.RunsTab()));
        _runsList = CreateRunList();
        _runsSection.Add(CreateListFrame(_runsList));
    }

    private void BuildBattlesSection(VisualElement parent)
    {
        _battlesSection = CreateSectionPanel(null);
        _battlesSection.style.flexGrow = 1f;
        _battlesSection.style.flexShrink = 1f;
        _battlesSection.style.minHeight = 0f;
        _battlesSection.style.minWidth = 0f;
        _battlesSection.style.marginLeft = UiSpacing.ColumnGap;
        parent.Add(_battlesSection);

        _battlesTitle = CreateSectionTitle(HistoryPanelText.Battles());
        _battlesTitle.style.marginTop = UiSpacing.None;
        _battlesSection.Add(_battlesTitle);
        _runsBattleSubtitle = CreateLabel(
            Sizes.FontSmall,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _runsBattleSubtitle.style.marginTop = UiSpacing.Xs;
        _runsBattleSubtitle.style.display = DisplayStyle.None;
        _battlesSection.Add(_runsBattleSubtitle);
        _battleList = CreateBattleList();
        _battleList.style.marginTop = UiSpacing.Md;
        _battlesSection.Add(CreateListFrame(_battleList));
    }

    private void BuildPreview(VisualElement parent)
    {
        _previewContainer = new VisualElement();
        _previewContainer.style.flexGrow = 0f;
        _previewContainer.style.flexShrink = 0f;
        _previewContainer.style.height = Length.Percent(Sizes.PreviewHeightPercent);
        _previewContainer.style.minHeight = 0f;
        _previewContainer.style.backgroundColor = Colors.HistoryPreviewBackground;
        UiStyle.Radius(_previewContainer.style, Radii.Md);
        UiStyle.Border(_previewContainer.style, Borders.Thin, Colors.HistoryListFrameBorder);
        _previewContainer.style.position = Position.Relative;
        _previewContainer.style.overflow = Overflow.Hidden;
        _previewContainer.style.marginTop = UiSpacing.ColumnGap;
        parent.Add(_previewContainer);

        _previewImage = new Image();
        _previewImage.scaleMode = ScaleMode.ScaleToFit;
        _previewImage.style.position = Position.Absolute;
        _previewImage.style.left = UiSpacing.Xs;
        _previewImage.style.right = UiSpacing.Xs;
        _previewImage.style.top = UiSpacing.Lg;
        _previewImage.style.bottom = UiSpacing.Lg;
        _previewContainer.Add(_previewImage);

        _previewStatusLabel = CreateLabel(
            Sizes.FontPreview,
            FontStyle.Normal,
            Colors.HistoryPreviewStatusText
        );
        _previewStatusLabel.style.position = Position.Absolute;
        _previewStatusLabel.style.left = UiSpacing.PanelPadding + UiSpacing.Xs;
        _previewStatusLabel.style.right = UiSpacing.PanelPadding + UiSpacing.Xs;
        _previewStatusLabel.style.top = UiSpacing.ColumnGap;
        _previewStatusLabel.style.bottom = UiSpacing.ColumnGap;
        _previewStatusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        _previewStatusLabel.style.whiteSpace = WhiteSpace.Normal;
        _previewStatusLabel.style.maxHeight = Sizes.PanelStatusMaxHeight;
        _previewStatusLabel.style.overflow = Overflow.Hidden;
        _previewContainer.Add(_previewStatusLabel);

        _previewDebugLabel = CreateLabel(
            Sizes.FontCorner,
            FontStyle.Bold,
            Colors.HistoryPreviewDebugText
        );
        _previewDebugLabel.style.position = Position.Absolute;
        _previewDebugLabel.style.right = UiSpacing.Xxl;
        _previewDebugLabel.style.top = UiSpacing.Xl;
        _previewDebugLabel.style.maxWidth = Sizes.StatusMaxWidth;
        _previewDebugLabel.style.whiteSpace = WhiteSpace.NoWrap;
        _previewDebugLabel.style.overflow = Overflow.Hidden;
        _previewDebugLabel.style.display = DisplayStyle.None;
        _previewContainer.Add(_previewDebugLabel);
    }

    private void BuildOperationRail(VisualElement parent)
    {
        var rail = new VisualElement();
        rail.style.flexDirection = FlexDirection.Column;
        rail.style.flexGrow = 0f;
        rail.style.flexShrink = 0f;
        rail.style.flexBasis = Length.Percent(Sizes.OperationRailWidthPercent);
        rail.style.minWidth = Sizes.OperationRailMinWidth;
        rail.style.maxWidth = Sizes.OperationRailMaxWidth;
        rail.style.minHeight = 0f;
        rail.style.marginLeft = UiSpacing.ColumnGap;
        parent.Add(rail);

        // ── Fixed header (never scrolls) ─────────────────────────────────────
        var titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.alignItems = Align.Center;
        titleRow.style.flexShrink = 0f;
        rail.Add(titleRow);

        _title = CreateLabel(Sizes.FontTitle, FontStyle.Bold, Colors.HistoryTitleText);
        _title.style.flexGrow = 1f;
        _title.style.flexShrink = 1f;
        _title.style.minWidth = 0f;
        _title.style.minHeight = Sizes.ButtonStandardHeight; // VIS-7: lock row height
        _title.style.whiteSpace = WhiteSpace.NoWrap;
        _title.style.overflow = Overflow.Hidden;
        titleRow.Add(_title);

        _closeButton = CreateButton(
            HistoryPanelText.Close(),
            _close,
            Sizes.CloseButtonWidth,
            Sizes.ButtonStandardHeight
        );
        StyleButton(_closeButton, Colors.CloseBackground, Colors.CloseText);
        titleRow.Add(_closeButton);

        _subtitle = BPPSupporterAttributionRow.Create();
        _subtitle.style.flexShrink = 0f;
        rail.Add(_subtitle); // VIS-2: no extra marginTop; component owns its Sm(6)

        BuildAccountLinkCard(rail);

        BuildFilterSlot(rail);

        // ── Overview: read-only chips + the connectivity probe, as one fixed row ─────
        // Pinned directly under the filters so the run/battle counts, DB status, and the
        // one-shot "is my data path healthy?" probe stay visible without scrolling. The probe
        // still hits the remote /health endpoint and reports its result into the footer banner.
        var overviewGroup = new VisualElement();
        overviewGroup.style.flexDirection = FlexDirection.Column;
        overviewGroup.style.flexShrink = 0f;
        overviewGroup.style.marginTop = UiSpacing.Xl;
        rail.Add(overviewGroup);

        var statsChipRow = new VisualElement();
        statsChipRow.style.flexDirection = FlexDirection.Row;
        statsChipRow.style.flexWrap = Wrap.Wrap;
        statsChipRow.style.alignItems = Align.Center;
        overviewGroup.Add(statsChipRow);

        _countChip = CreateChip();
        _countChip.style.minWidth = Sizes.ChipMinWidth;
        _battleChip = CreateChip();
        _battleChip.style.minWidth = Sizes.ChipMinWidth;
        _battleChip.style.marginLeft = UiSpacing.Sm;
        _databaseChip = CreateChip();
        _databaseChip.style.minWidth = Sizes.ChipMinWidth;
        _databaseChip.style.marginLeft = UiSpacing.Sm;
        statsChipRow.Add(_countChip);
        statsChipRow.Add(_battleChip);
        statsChipRow.Add(_databaseChip);

        _checkServerHealthButton = CreateButton(
            HistoryPanelText.CheckServerHealth(),
            _checkServerHealth,
            Sizes.ServerHealthButtonWidth,
            Sizes.ButtonStandardHeight
        );
        StyleButton(_checkServerHealthButton, Colors.ReplayBackground, Colors.ReplayText);
        _checkServerHealthButton.style.marginLeft = UiSpacing.Sm;
        statsChipRow.Add(_checkServerHealthButton);

        // ── Flexible body. railBody (plain element, flexGrow=1) reliably fills the
        //    rail's vertical slack — unlike a ScrollView contentContainer, which
        //    collapses to content height (see CollectionPanelView.Tree.cs:283-315).
        var railBody = new VisualElement();
        railBody.style.flexDirection = FlexDirection.Column;
        railBody.style.flexGrow = 1f;
        railBody.style.flexShrink = 1f;
        railBody.style.minHeight = 0f;
        railBody.style.marginTop = UiSpacing.Xl;
        rail.Add(railBody);

        // Selected-battle detail card: the rail's primary flex-growing element.
        var selectedDetailCard = new VisualElement();
        selectedDetailCard.style.flexDirection = FlexDirection.Column;
        selectedDetailCard.style.flexGrow = 0f;
        selectedDetailCard.style.flexShrink = 0f;
        selectedDetailCard.style.minHeight = 0f;
        selectedDetailCard.style.overflow = Overflow.Hidden;
        selectedDetailCard.style.backgroundColor = Colors.HistoryFooterBackground;
        UiStyle.Radius(selectedDetailCard.style, Radii.Md);
        UiStyle.Border(selectedDetailCard.style, Borders.Thin, Colors.HistoryListFrameBorder);
        UiStyle.Padding(selectedDetailCard.style, UiSpacing.Lg);
        railBody.Add(selectedDetailCard);

        var resultRow = new VisualElement();
        resultRow.style.flexDirection = FlexDirection.Row;
        resultRow.style.flexWrap = Wrap.NoWrap;
        resultRow.style.alignItems = Align.Center;
        selectedDetailCard.Add(resultRow);

        _resultPill = CreateDetailPill(resultRow, Sizes.InlinePillMinWidth);
        _resultPill.style.display = DisplayStyle.None;

        _opponentName = CreateLabel(Sizes.FontFooterPrimary, FontStyle.Bold, Colors.White);
        _opponentName.style.flexGrow = 1f;
        _opponentName.style.flexShrink = 1f;
        _opponentName.style.minWidth = 0f;
        _opponentName.style.whiteSpace = WhiteSpace.NoWrap;
        _opponentName.style.overflow = Overflow.Hidden; // RESP-4: truncate, tooltip carries full name
        _opponentName.style.display = DisplayStyle.None;
        resultRow.Add(_opponentName);

        _dayPill = CreateDetailPill(resultRow, Sizes.RunProgressPillWidth);
        _dayPill.style.marginLeft = UiSpacing.Sm;
        _dayPill.style.display = DisplayStyle.None;

        _detailMeta = CreateLabel(
            Sizes.FontSmall,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _detailMeta.style.whiteSpace = WhiteSpace.Normal;
        _detailMeta.style.maxHeight = Sizes.DetailTextMaxHeight;
        _detailMeta.style.overflow = Overflow.Hidden;
        _detailMeta.style.marginTop = UiSpacing.Xs;
        _detailMeta.style.display = DisplayStyle.None;
        selectedDetailCard.Add(_detailMeta);

        _detailSnapshot = CreateLabel(
            Sizes.FontSmall,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _detailSnapshot.style.whiteSpace = WhiteSpace.Normal;
        _detailSnapshot.style.maxHeight = Sizes.DetailTextMaxHeight;
        _detailSnapshot.style.overflow = Overflow.Hidden;
        _detailSnapshot.style.marginTop = UiSpacing.Xxs;
        _detailSnapshot.style.display = DisplayStyle.None;
        selectedDetailCard.Add(_detailSnapshot);

        _detailPlaceholder = CreateLabel(
            Sizes.FontSmall,
            FontStyle.Normal,
            Colors.WithAlpha(Colors.HistoryFooterSecondaryText, 0.6f) // single 0.6 layer, no style.opacity
        );
        _detailPlaceholder.style.whiteSpace = WhiteSpace.Normal;
        _detailPlaceholder.style.maxHeight = Sizes.DetailTextMaxHeight;
        _detailPlaceholder.style.overflow = Overflow.Hidden;
        _detailPlaceholder.style.unityTextAlign = TextAnchor.MiddleCenter;
        _detailPlaceholder.style.marginTop = UiSpacing.Sm;
        _detailPlaceholder.style.display = DisplayStyle.None;
        selectedDetailCard.Add(_detailPlaceholder);

        _ghostOpponentEliminatedNotice = CreateLabel(
            Sizes.FontBody,
            FontStyle.Bold,
            Colors.HistoryEliminatedText
        );
        UiStyle.Padding(_ghostOpponentEliminatedNotice.style, UiSpacing.Md, UiSpacing.Sm);
        _ghostOpponentEliminatedNotice.style.unityTextAlign = TextAnchor.MiddleCenter;
        _ghostOpponentEliminatedNotice.style.whiteSpace = WhiteSpace.Normal; // was NoWrap
        _ghostOpponentEliminatedNotice.style.maxHeight = Sizes.DetailNoticeMaxHeight;
        _ghostOpponentEliminatedNotice.style.overflow = Overflow.Hidden;
        _ghostOpponentEliminatedNotice.style.backgroundColor = Colors.HistoryEliminatedBackground;
        UiStyle.Radius(_ghostOpponentEliminatedNotice.style, Radii.Row);
        UiStyle.Border(
            _ghostOpponentEliminatedNotice.style,
            Borders.Thin,
            Colors.HistoryEliminatedNoticeBorder
        );
        _ghostOpponentEliminatedNotice.style.display = DisplayStyle.None;
        selectedDetailCard.Add(_ghostOpponentEliminatedNotice);

        // ── Fixed footer: status banner directly above its actions ───────────
        _statusLabel = CreateLabel(Sizes.FontCorner, FontStyle.Normal, Colors.HistoryStatusText);
        _statusLabel.style.display = DisplayStyle.None;
        _statusLabel.style.flexGrow = 0f;
        _statusLabel.style.flexShrink = 0f;
        _statusLabel.style.whiteSpace = WhiteSpace.Normal;
        _statusLabel.style.minHeight = Sizes.StatusHeight;
        _statusLabel.style.maxHeight = Sizes.PanelStatusMaxHeight;
        _statusLabel.style.overflow = Overflow.Hidden;
        _statusLabel.style.width = Length.Percent(100f);
        _statusLabel.style.marginTop = UiSpacing.Md;
        _statusLabel.style.alignSelf = Align.Stretch;
        UiStyle.Padding(_statusLabel.style, UiSpacing.Xl, UiSpacing.Sm); // VIS-4
        _statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        _statusLabel.style.backgroundColor = Colors.HistoryStatusBackground;
        UiStyle.Radius(_statusLabel.style, Radii.Md); // VIS-3: was Radii.Status
        UiStyle.Border(_statusLabel.style, Borders.Thin, Colors.HistoryStatusBorder);
        rail.Add(_statusLabel);

        var actions = new VisualElement();
        actions.style.flexDirection = FlexDirection.Column;
        actions.style.flexShrink = 0f;
        actions.style.marginTop = UiSpacing.Md;
        rail.Add(actions);

        _deleteButton = CreateRailButton(HistoryPanelText.Delete(), _delete);
        _recordAndReplayButton = CreateRailButton(
            HistoryPanelText.RecordAndReplay(),
            _recordAndReplay
        );
        _replayButton = CreateRailButton(HistoryPanelText.Replay(), _replay);
        StyleButton(_deleteButton, Colors.DeleteBackground, Colors.DeleteText);
        StyleButton(_recordAndReplayButton, Colors.RecordReplayBackground, Colors.RecordReplayText);
        StyleButton(_replayButton, Colors.ReplayBackground, Colors.ReplayText);

        var replayActionRow = new VisualElement();
        replayActionRow.style.flexDirection = FlexDirection.Row;
        replayActionRow.style.flexShrink = 0f;
        replayActionRow.style.width = Length.Percent(100f);
        actions.Add(replayActionRow);

        UseActionRowRatio(_replayButton, 3f);
        UseActionRowRatio(_recordAndReplayButton, 1f);
        replayActionRow.Add(_replayButton);
        _recordAndReplayButton.style.marginLeft = UiSpacing.Md;
        replayActionRow.Add(_recordAndReplayButton);

        _deleteButton.style.marginTop = UiSpacing.Md;
        actions.Add(_deleteButton);
    }

    private const int LinkCodeLength = 10;

    private void BuildAccountLinkCard(VisualElement rail)
    {
        _accountCard = new VisualElement();
        _accountCard.style.flexDirection = FlexDirection.Column;
        _accountCard.style.flexShrink = 0f;
        _accountCard.style.marginTop = UiSpacing.Xl;
        rail.Add(_accountCard);

        _accountCollapsedRow = new VisualElement();
        _accountCollapsedRow.style.flexDirection = FlexDirection.Row;
        _accountCollapsedRow.style.alignItems = Align.Center;
        _accountCollapsedRow.style.minWidth = 0f;
        _accountCard.Add(_accountCollapsedRow);

        _accountRowStatus = CreateLabel(
            Sizes.FontSmall,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _accountRowStatus.style.flexGrow = 1f;
        _accountRowStatus.style.flexShrink = 1f;
        _accountRowStatus.style.minWidth = 0f;
        _accountRowStatus.style.whiteSpace = WhiteSpace.NoWrap;
        _accountRowStatus.style.overflow = Overflow.Hidden;
        _accountCollapsedRow.Add(_accountRowStatus);

        _accountRowAction = CreateButton(
            HistoryPanelText.AccountLink.RowBind(),
            _toggleAccountLinkForm,
            0f,
            Sizes.ButtonCompactHeight,
            fixedWidth: false
        );
        _accountRowAction.style.flexGrow = 0f;
        _accountRowAction.style.flexShrink = 0f;
        _accountRowAction.style.flexBasis = StyleKeyword.Auto;
        _accountRowAction.style.minWidth = Sizes.InlinePillMinWidth;
        _accountRowAction.style.marginLeft = UiSpacing.Sm;
        StyleButton(
            _accountRowAction,
            Colors.HistoryButtonBackground,
            Colors.HistoryFooterSecondaryText
        );
        _accountCollapsedRow.Add(_accountRowAction);

        var titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.alignItems = Align.Center;
        titleRow.style.minWidth = 0f;
        _accountCard.Add(titleRow);

        _accountTitle = CreateLabel(Sizes.FontBody, FontStyle.Bold, Colors.HistoryTitleText);
        _accountTitle.style.flexGrow = 1f;
        _accountTitle.style.flexShrink = 1f;
        _accountTitle.style.minWidth = 0f;
        _accountTitle.style.whiteSpace = WhiteSpace.NoWrap;
        _accountTitle.style.overflow = Overflow.Hidden;
        titleRow.Add(_accountTitle);

        _accountCollapseButton = CreateButton(
            HistoryPanelText.AccountLink.Collapse(),
            _toggleAccountLinkForm,
            0f,
            Sizes.ButtonCompactHeight,
            fixedWidth: false
        );
        _accountCollapseButton.style.flexGrow = 0f;
        _accountCollapseButton.style.flexShrink = 0f;
        _accountCollapseButton.style.flexBasis = StyleKeyword.Auto;
        _accountCollapseButton.style.minWidth = Sizes.InlinePillMinWidth;
        _accountCollapseButton.style.marginLeft = UiSpacing.Sm;
        StyleButton(
            _accountCollapseButton,
            Colors.HistoryButtonBackground,
            Colors.HistoryFooterSecondaryText
        );
        titleRow.Add(_accountCollapseButton);

        _accountWhy = CreateLabel(
            Sizes.FontSmall,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _accountWhy.style.whiteSpace = WhiteSpace.Normal;
        _accountWhy.style.maxHeight = Sizes.DetailTextMaxHeight;
        _accountWhy.style.overflow = Overflow.Hidden;
        _accountWhy.style.marginTop = UiSpacing.Xxs;
        _accountCard.Add(_accountWhy);

        _accountCodeRow = new VisualElement();
        _accountCodeRow.style.flexDirection = FlexDirection.Row;
        _accountCodeRow.style.alignItems = Align.Center;
        _accountCodeRow.style.marginTop = UiSpacing.Lg;
        _accountCard.Add(_accountCodeRow);

        // Segmented 10-cell code input — the focal point. Each cell holds one character; typing
        // auto-advances, Backspace clears then steps back, and pasting a full code fills every cell.
        _accountCodeCells = new TextField[LinkCodeLength];
        for (var i = 0; i < LinkCodeLength; i++)
        {
            var index = i;
            var cell = new TextField();
            cell.maxLength = LinkCodeLength; // allow a full paste in one cell; redistributed below
            cell.isDelayed = false;
            cell.selectAllOnFocus = true;
            cell.style.flexGrow = 1f;
            cell.style.flexShrink = 1f;
            cell.style.minWidth = 0f;
            cell.style.height = Sizes.ButtonFooterHeight;
            cell.style.marginLeft = index == 0 ? 0f : UiSpacing.Xxs;
            // The visible single box is the cell's own Radius/Border on the de-chromed root.
            UiStyle.Radius(cell.style, Radii.Row);
            UiStyle.Border(cell.style, Borders.Thin, Colors.HistoryListFrameBorder);
            // Centering + font must reach the inner 'unity-text-input' element (the game's USS
            // overrides the inherited cascade), and the inner chrome must be stripped. Defer to
            // AttachToPanelEvent so cell.Q(...) resolves a non-null inner element.
            cell.RegisterCallback<AttachToPanelEvent>(_ =>
                StyleCodeCell(cell, Sizes.FontButton, Colors.White)
            );
            cell.RegisterValueChangedCallback(evt => OnCodeCellChanged(index, evt.newValue));
            cell.RegisterCallback<KeyDownEvent>(evt => OnCodeCellKeyDown(index, evt));
            _accountCodeCells[index] = cell;
            _accountCodeRow.Add(cell);
        }

        _accountLinkButton = CreateButton(
            HistoryPanelText.AccountLink.Button(),
            SubmitAccountLink,
            0f,
            Sizes.ButtonFooterHeight,
            fixedWidth: false
        );
        // Full-width primary CTA. flexGrow stays 0 so it does not stretch vertically as a column
        // child (the button-in-column flex trap); width 100% makes it span the card instead.
        _accountLinkButton.style.flexGrow = 0f;
        _accountLinkButton.style.flexShrink = 0f;
        _accountLinkButton.style.flexBasis = StyleKeyword.Auto;
        _accountLinkButton.style.width = Length.Percent(100f);
        _accountLinkButton.style.marginTop = UiSpacing.Lg;
        StyleButton(_accountLinkButton, Colors.ReplayBackground, Colors.ReplayText);
        _accountCard.Add(_accountLinkButton);

        _accountAlreadyLinkedButton = CreateButton(
            HistoryPanelText.AccountLink.AlreadyLinkedElsewhereButton(),
            () => _markAccountLinkedManually(),
            0f,
            Sizes.ButtonCompactHeight,
            fixedWidth: false
        );
        _accountAlreadyLinkedButton.style.flexGrow = 0f;
        _accountAlreadyLinkedButton.style.flexShrink = 0f;
        _accountAlreadyLinkedButton.style.flexBasis = StyleKeyword.Auto;
        _accountAlreadyLinkedButton.style.width = Length.Percent(100f);
        _accountAlreadyLinkedButton.style.marginTop = UiSpacing.Xs;
        StyleButton(
            _accountAlreadyLinkedButton,
            Colors.HistoryButtonBackground,
            Colors.HistoryFooterSecondaryText
        );
        _accountCard.Add(_accountAlreadyLinkedButton);

        _accountHint = CreateLabel(
            Sizes.FontCorner,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _accountHint.style.whiteSpace = WhiteSpace.Normal;
        _accountHint.style.maxHeight = Sizes.DetailTextMaxHeight;
        _accountHint.style.overflow = Overflow.Hidden;
        _accountHint.style.marginTop = UiSpacing.Xs;
        _accountCard.Add(_accountHint);

        _accountBanner = CreateLabel(Sizes.FontCorner, FontStyle.Normal, Colors.HistoryStatusText);
        _accountBanner.style.display = DisplayStyle.None;
        _accountBanner.style.flexGrow = 0f;
        _accountBanner.style.flexShrink = 0f;
        _accountBanner.style.whiteSpace = WhiteSpace.Normal;
        _accountBanner.style.minHeight = Sizes.StatusHeight;
        _accountBanner.style.maxHeight = Sizes.PanelStatusMaxHeight;
        _accountBanner.style.overflow = Overflow.Hidden;
        _accountBanner.style.width = Length.Percent(100f);
        _accountBanner.style.marginTop = UiSpacing.Sm;
        _accountBanner.style.alignSelf = Align.Stretch;
        UiStyle.Padding(_accountBanner.style, UiSpacing.Xl, UiSpacing.Sm);
        _accountBanner.style.unityTextAlign = TextAnchor.MiddleLeft;
        _accountBanner.style.backgroundColor = Colors.HistoryStatusBackground;
        UiStyle.Radius(_accountBanner.style, Radii.Md);
        UiStyle.Border(_accountBanner.style, Borders.Thin, Colors.HistoryStatusBorder);
        _accountCard.Add(_accountBanner);
    }

    private void OnCodeCellChanged(int index, string? newValue)
    {
        if (_suppressCellNotify || _accountCodeCells == null)
            return;

        // Strip whitespace only — the redeem alphabet is case-sensitive, so never transform case.
        var text = StripWhitespace(newValue);
        _suppressCellNotify = true;
        try
        {
            if (text.Length <= 1)
            {
                _accountCodeCells[index].SetValueWithoutNotify(text);
                if (text.Length == 1 && index < LinkCodeLength - 1)
                    _accountCodeCells[index + 1].Focus();
            }
            else
            {
                DistributeCode(text, index);
            }
        }
        finally
        {
            _suppressCellNotify = false;
        }

        UpdateAccountCodeFeedback();
    }

    private void OnCodeCellKeyDown(int index, KeyDownEvent evt)
    {
        if (_accountCodeCells == null)
            return;

        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
        {
            evt.StopPropagation();
            SubmitAccountLink();
            return;
        }

        if (evt.keyCode == KeyCode.Backspace)
        {
            // Own Backspace fully: native TextField deletion would eat the char before we can step
            // back on an empty cell, forcing a second press. One press clears the current char; on an
            // already-empty cell it steps back and clears the previous cell.
            evt.StopPropagation();
            _suppressCellNotify = true;
            try
            {
                if (!string.IsNullOrEmpty(_accountCodeCells[index].value))
                {
                    _accountCodeCells[index].SetValueWithoutNotify(string.Empty);
                }
                else if (index > 0)
                {
                    _accountCodeCells[index - 1].SetValueWithoutNotify(string.Empty);
                    _accountCodeCells[index - 1].Focus();
                }
            }
            finally
            {
                _suppressCellNotify = false;
            }

            UpdateAccountCodeFeedback();
        }
        else if (evt.keyCode == KeyCode.LeftArrow && index > 0)
        {
            evt.StopPropagation();
            _accountCodeCells[index - 1].Focus();
        }
        else if (evt.keyCode == KeyCode.RightArrow && index < LinkCodeLength - 1)
        {
            evt.StopPropagation();
            _accountCodeCells[index + 1].Focus();
        }
    }

    // Spreads a multi-character value (a paste, or fast typing) across the cells from startIndex, one
    // character per cell, then focuses the next empty cell. Letter case is preserved.
    private void DistributeCode(string text, int startIndex)
    {
        if (_accountCodeCells == null)
            return;

        var writeIndex = startIndex;
        foreach (var ch in text)
        {
            if (writeIndex >= LinkCodeLength)
                break;

            _accountCodeCells[writeIndex].SetValueWithoutNotify(ch.ToString());
            writeIndex++;
        }

        var focusIndex = writeIndex < LinkCodeLength ? writeIndex : LinkCodeLength - 1;
        _accountCodeCells[focusIndex].Focus();
    }

    private void ClearCodeCells()
    {
        if (_accountCodeCells == null)
            return;

        _suppressCellNotify = true;
        foreach (var cell in _accountCodeCells)
            cell.SetValueWithoutNotify(string.Empty);
        _suppressCellNotify = false;
    }

    private string CombinedAccountCode()
    {
        if (_accountCodeCells == null)
            return string.Empty;

        var code = string.Empty;
        foreach (var cell in _accountCodeCells)
            code += cell.value ?? string.Empty;

        return code.Trim();
    }

    private static string StripWhitespace(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var result = string.Empty;
        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
                result += ch;
        }

        return result;
    }

    private void BuildFilterSlot(VisualElement rail)
    {
        _filterSlot = new VisualElement();
        _filterSlot.style.flexDirection = FlexDirection.Column;
        _filterSlot.style.flexShrink = 0f;
        _filterSlot.style.marginTop = UiSpacing.Xl;
        _filterSlot.style.backgroundColor = Colors.HistorySectionBackground;
        UiStyle.Radius(_filterSlot.style, Radii.Md);
        UiStyle.Padding(_filterSlot.style, UiSpacing.Md);
        rail.Add(_filterSlot);

        var tabsRow = new VisualElement();
        tabsRow.style.flexDirection = FlexDirection.Row;
        tabsRow.style.flexWrap = Wrap.NoWrap;
        tabsRow.style.alignItems = Align.Center;
        _filterSlot.Add(tabsRow);

        _runsTabButton = CreateButton(
            HistoryPanelText.RunsTab(),
            () => _setSectionMode(HistorySectionMode.Runs),
            0f,
            Sizes.ButtonStandardHeight,
            fixedWidth: false
        );
        _ghostTabButton = CreateButton(
            HistoryPanelText.GhostTab(),
            () => _setSectionMode(HistorySectionMode.Ghost),
            0f,
            Sizes.ButtonStandardHeight,
            fixedWidth: false
        );
        tabsRow.Add(_runsTabButton);
        _ghostTabButton.style.marginLeft = UiSpacing.Sm;
        tabsRow.Add(_ghostTabButton);

        _runsFilterRow = new VisualElement();
        _runsFilterRow.style.flexDirection = FlexDirection.Row;
        _runsFilterRow.style.flexWrap = Wrap.NoWrap;
        _runsFilterRow.style.alignItems = Align.Center;
        _runsFilterRow.style.marginTop = UiSpacing.Sm;
        _filterSlot.Add(_runsFilterRow);

        _heroChips = new Button[HeroRoster.Length];
        for (var i = 0; i < HeroRoster.Length; i++)
        {
            var heroName = HeroRoster[i];
            var heroChip = CreateButton(
                HeroVisual.Resolve(heroName).ShortCode,
                () => _setRunHero(heroName),
                0f,
                Sizes.ButtonCompactHeight,
                fixedWidth: false
            );
            heroChip.tooltip = heroName;
            if (i > 0)
                heroChip.style.marginLeft = UiSpacing.Xs;
            _runsFilterRow.Add(heroChip);
            _heroChips[i] = heroChip;
        }

        _ghostFilterRow = new VisualElement();
        _ghostFilterRow.style.flexDirection = FlexDirection.Row;
        _ghostFilterRow.style.flexWrap = Wrap.NoWrap;
        _ghostFilterRow.style.alignItems = Align.Center;
        _ghostFilterRow.style.display = DisplayStyle.None;
        _ghostFilterRow.style.marginTop = UiSpacing.Sm;
        _filterSlot.Add(_ghostFilterRow);

        _ghostAllButton = CreateButton(
            HistoryPanelText.FilterAll(),
            () => _setGhostFilter(GhostBattleFilter.All),
            0f,
            Sizes.ButtonCompactHeight,
            fixedWidth: false
        );
        _ghostWonButton = CreateButton(
            HistoryPanelText.FilterIWon(),
            () => _setGhostFilter(GhostBattleFilter.IWon),
            0f,
            Sizes.ButtonCompactHeight,
            fixedWidth: false
        );
        _ghostLostButton = CreateButton(
            HistoryPanelText.FilterILost(),
            () => _setGhostFilter(GhostBattleFilter.ILost),
            0f,
            Sizes.ButtonCompactHeight,
            fixedWidth: false
        );
        _ghostDayButton = CreateButton(
            HistoryPanelText.FilterDayMin10(),
            _toggleGhostDayMin10,
            0f,
            Sizes.ButtonCompactHeight,
            fixedWidth: false
        );
        _ghostFilterRow.Add(_ghostAllButton);
        _ghostWonButton.style.marginLeft = UiSpacing.Sm;
        _ghostFilterRow.Add(_ghostWonButton);
        _ghostLostButton.style.marginLeft = UiSpacing.Sm;
        _ghostFilterRow.Add(_ghostLostButton);
        _ghostDayButton.style.marginLeft = UiSpacing.Md;
        _ghostFilterRow.Add(_ghostDayButton);
    }

    private ListView CreateRunList()
    {
        var list = new ListView();
        list.style.flexGrow = 1f;
        list.style.flexShrink = 1f;
        list.style.minHeight = 0f;
        list.style.height = Length.Percent(100);
        list.selectionType = SelectionType.Single;
        list.fixedItemHeight = Sizes.RunRowHeight;
        list.makeItem = MakeRunRow;
        list.bindItem = BindRunRow;
        return list;
    }

    private ListView CreateBattleList()
    {
        var list = new ListView();
        list.style.flexGrow = 1f;
        list.style.flexShrink = 1f;
        list.style.minHeight = 0f;
        list.style.height = Length.Percent(100);
        list.selectionType = SelectionType.Single;
        list.fixedItemHeight = Sizes.BattleRowHeight;
        list.makeItem = MakeBattleRow;
        list.bindItem = BindBattleRow;
        return list;
    }
}
