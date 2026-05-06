#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanelUiToolkitView
{
    private void BuildTree(VisualElement root)
    {
        var overlay = new VisualElement();
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0f;
        overlay.style.right = 0f;
        overlay.style.top = 0f;
        overlay.style.bottom = 0f;
        overlay.style.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 0.82f);
        overlay.style.justifyContent = Justify.Center;
        overlay.style.alignItems = Align.Center;
        root.Add(overlay);

        var panel = new VisualElement();
        panel.style.width = 1280f;
        panel.style.height = 1020f;
        panel.style.backgroundColor = new Color(0.08f, 0.10f, 0.13f, 0.985f);
        panel.style.borderTopLeftRadius = 14f;
        panel.style.borderTopRightRadius = 14f;
        panel.style.borderBottomLeftRadius = 14f;
        panel.style.borderBottomRightRadius = 14f;
        panel.style.paddingLeft = 24f;
        panel.style.paddingRight = 24f;
        panel.style.paddingTop = 24f;
        panel.style.paddingBottom = 14f;
        panel.style.flexDirection = FlexDirection.Column;
        overlay.Add(panel);

        BuildHeader(panel);
        BuildContent(panel);
        BuildFooter(panel);
    }

    private void BuildHeader(VisualElement parent)
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Column;
        parent.Add(header);

        _title = CreateLabel(28, FontStyle.Bold, new Color(0.97f, 0.85f, 0.57f, 1f));
        header.Add(_title);

        _subtitle = CreateLabel(14, FontStyle.Normal, new Color(0.82f, 0.86f, 0.91f, 0.94f));
        _subtitle.style.whiteSpace = WhiteSpace.Normal;
        _subtitle.style.marginTop = 8f;
        header.Add(_subtitle);

        var chipRow = new VisualElement();
        chipRow.style.flexDirection = FlexDirection.Row;
        chipRow.style.alignItems = Align.Center;
        chipRow.style.marginTop = 10f;
        header.Add(chipRow);

        _countChip = CreateChip();
        _battleChip = CreateChip();
        _databaseChip = CreateChip();
        chipRow.Add(_countChip);
        _battleChip.style.marginLeft = 8f;
        chipRow.Add(_battleChip);
        _databaseChip.style.marginLeft = 8f;
        chipRow.Add(_databaseChip);
        _statusLabel = CreateLabel(11, FontStyle.Normal, new Color(0.86f, 0.90f, 0.96f, 0.92f));
        _statusLabel.style.display = DisplayStyle.None;
        _statusLabel.style.marginLeft = 12f;
        _statusLabel.style.flexGrow = 0f;
        _statusLabel.style.flexShrink = 1f;
        _statusLabel.style.whiteSpace = WhiteSpace.NoWrap;
        _statusLabel.style.height = 24f;
        _statusLabel.style.maxWidth = 220f;
        _statusLabel.style.paddingLeft = 10f;
        _statusLabel.style.paddingRight = 10f;
        _statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        _statusLabel.style.backgroundColor = new Color(0.16f, 0.20f, 0.26f, 0.72f);
        _statusLabel.style.borderTopLeftRadius = 12f;
        _statusLabel.style.borderTopRightRadius = 12f;
        _statusLabel.style.borderBottomLeftRadius = 12f;
        _statusLabel.style.borderBottomRightRadius = 12f;
        _statusLabel.style.borderLeftWidth = 1f;
        _statusLabel.style.borderRightWidth = 1f;
        _statusLabel.style.borderTopWidth = 1f;
        _statusLabel.style.borderBottomWidth = 1f;
        _statusLabel.style.borderLeftColor = new Color(0.34f, 0.40f, 0.49f, 0.36f);
        _statusLabel.style.borderRightColor = new Color(0.34f, 0.40f, 0.49f, 0.36f);
        _statusLabel.style.borderTopColor = new Color(0.34f, 0.40f, 0.49f, 0.36f);
        _statusLabel.style.borderBottomColor = new Color(0.34f, 0.40f, 0.49f, 0.36f);
        chipRow.Add(_statusLabel);
        chipRow.Add(CreateSpacer());

        _runsTabButton = CreateButton(
            HistoryPanelText.RunsTab(),
            () => _setSectionMode(HistorySectionMode.Runs),
            72f,
            32f
        );
        _ghostTabButton = CreateButton(
            HistoryPanelText.GhostTab(),
            () => _setSectionMode(HistorySectionMode.Ghost),
            72f,
            32f
        );
        _finalBuildRefreshButton = CreateButton(
            HistoryPanelText.RefreshFinalBuilds(),
            _refreshFinalBuilds,
            124f,
            32f
        );
        chipRow.Add(_runsTabButton);
        _ghostTabButton.style.marginLeft = 8f;
        chipRow.Add(_ghostTabButton);
        _finalBuildRefreshButton.style.marginLeft = 8f;
        chipRow.Add(_finalBuildRefreshButton);
    }

    private void BuildContent(VisualElement parent)
    {
        var content = new VisualElement();
        content.style.height = 792f;
        content.style.flexGrow = 0f;
        content.style.flexShrink = 0f;
        content.style.minHeight = 792f;
        content.style.maxHeight = 792f;
        content.style.flexDirection = FlexDirection.Column;
        content.style.marginTop = 16f;
        parent.Add(content);

        var columns = new VisualElement();
        columns.style.flexGrow = 1f;
        columns.style.flexShrink = 1f;
        columns.style.minHeight = 0f;
        columns.style.flexDirection = FlexDirection.Row;
        content.Add(columns);

        _runsSection = CreateSectionPanel(500f);
        _runsSection.style.flexGrow = 0f;
        _runsSection.style.flexShrink = 0f;
        _runsSection.style.minHeight = 0f;
        _runsSection.style.minWidth = 500f;
        _runsSection.style.maxWidth = 500f;
        columns.Add(_runsSection);
        _runsSection.Add(CreateSectionTitle(HistoryPanelText.RunsTab()));
        _runsList = CreateRunList();
        _runsSection.Add(CreateListFrame(_runsList));

        _battlesSection = CreateSectionPanel(null);
        _battlesSection.style.flexGrow = 1f;
        _battlesSection.style.flexShrink = 1f;
        _battlesSection.style.minHeight = 0f;
        _battlesSection.style.marginLeft = 18f;
        columns.Add(_battlesSection);

        _ghostFilterRow = new VisualElement();
        _ghostFilterRow.style.flexDirection = FlexDirection.Row;
        _ghostFilterRow.style.display = DisplayStyle.None;
        _battlesSection.Add(_ghostFilterRow);

        _ghostAllButton = CreateButton(
            HistoryPanelText.FilterAll(),
            () => _setGhostFilter(GhostBattleFilter.All),
            70f,
            24f
        );
        _ghostWonButton = CreateButton(
            HistoryPanelText.FilterIWon(),
            () => _setGhostFilter(GhostBattleFilter.IWon),
            78f,
            24f
        );
        _ghostLostButton = CreateButton(
            HistoryPanelText.FilterILost(),
            () => _setGhostFilter(GhostBattleFilter.ILost),
            78f,
            24f
        );
        _ghostFilterRow.Add(_ghostAllButton);
        _ghostWonButton.style.marginLeft = 8f;
        _ghostFilterRow.Add(_ghostWonButton);
        _ghostLostButton.style.marginLeft = 8f;
        _ghostFilterRow.Add(_ghostLostButton);
        _ghostFilterRow.Add(CreateSpacer());

        _battlesTitle = CreateSectionTitle(HistoryPanelText.Battles());
        _battlesTitle.style.marginTop = 0f;
        _battlesSection.Add(_battlesTitle);
        _runsBattleSubtitle = CreateLabel(
            12,
            FontStyle.Normal,
            new Color(0.72f, 0.77f, 0.84f, 0.92f)
        );
        _runsBattleSubtitle.style.marginTop = 4f;
        _runsBattleSubtitle.style.display = DisplayStyle.None;
        _battlesSection.Add(_runsBattleSubtitle);
        _battleList = CreateBattleList();
        _battleList.style.marginTop = 8f;
        _battlesSection.Add(CreateListFrame(_battleList));

        _ghostOpponentEliminatedNotice = CreateLabel(
            14,
            FontStyle.Bold,
            new Color(0.99f, 0.90f, 0.68f, 1f)
        );
        _ghostOpponentEliminatedNotice.style.height = 34f;
        _ghostOpponentEliminatedNotice.style.minHeight = 34f;
        _ghostOpponentEliminatedNotice.style.maxHeight = 34f;
        _ghostOpponentEliminatedNotice.style.marginTop = 10f;
        _ghostOpponentEliminatedNotice.style.paddingLeft = 14f;
        _ghostOpponentEliminatedNotice.style.paddingRight = 14f;
        _ghostOpponentEliminatedNotice.style.unityTextAlign = TextAnchor.MiddleCenter;
        _ghostOpponentEliminatedNotice.style.whiteSpace = WhiteSpace.NoWrap;
        _ghostOpponentEliminatedNotice.style.backgroundColor = new Color(0.32f, 0.24f, 0.10f, 0.96f);
        _ghostOpponentEliminatedNotice.style.borderTopLeftRadius = 8f;
        _ghostOpponentEliminatedNotice.style.borderTopRightRadius = 8f;
        _ghostOpponentEliminatedNotice.style.borderBottomLeftRadius = 8f;
        _ghostOpponentEliminatedNotice.style.borderBottomRightRadius = 8f;
        _ghostOpponentEliminatedNotice.style.borderLeftWidth = 1f;
        _ghostOpponentEliminatedNotice.style.borderRightWidth = 1f;
        _ghostOpponentEliminatedNotice.style.borderTopWidth = 1f;
        _ghostOpponentEliminatedNotice.style.borderBottomWidth = 1f;
        _ghostOpponentEliminatedNotice.style.borderLeftColor = new Color(0.94f, 0.70f, 0.28f, 0.48f);
        _ghostOpponentEliminatedNotice.style.borderRightColor = new Color(0.94f, 0.70f, 0.28f, 0.48f);
        _ghostOpponentEliminatedNotice.style.borderTopColor = new Color(0.94f, 0.70f, 0.28f, 0.48f);
        _ghostOpponentEliminatedNotice.style.borderBottomColor = new Color(0.94f, 0.70f, 0.28f, 0.48f);
        _ghostOpponentEliminatedNotice.style.display = DisplayStyle.None;
        content.Add(_ghostOpponentEliminatedNotice);

        _previewContainer = new VisualElement();
        _previewContainer.style.height = 284f;
        _previewContainer.style.flexShrink = 0f;
        _previewContainer.style.minHeight = 284f;
        _previewContainer.style.maxHeight = 284f;
        _previewContainer.style.backgroundColor = new Color(0.07f, 0.09f, 0.12f, 0.99f);
        _previewContainer.style.borderTopLeftRadius = 10f;
        _previewContainer.style.borderTopRightRadius = 10f;
        _previewContainer.style.borderBottomLeftRadius = 10f;
        _previewContainer.style.borderBottomRightRadius = 10f;
        _previewContainer.style.position = Position.Relative;
        _previewContainer.style.overflow = Overflow.Hidden;
        _previewContainer.style.marginTop = 10f;
        content.Add(_previewContainer);

        _previewImage = new Image();
        _previewImage.scaleMode = ScaleMode.ScaleToFit;
        _previewImage.style.position = Position.Absolute;
        _previewImage.style.left = 4f;
        _previewImage.style.right = 4f;
        _previewImage.style.top = 10f;
        _previewImage.style.bottom = 10f;
        _previewContainer.Add(_previewImage);

        _previewStatusLabel = CreateLabel(
            13,
            FontStyle.Normal,
            new Color(0.82f, 0.87f, 0.93f, 0.96f)
        );
        _previewStatusLabel.style.position = Position.Absolute;
        _previewStatusLabel.style.left = 28f;
        _previewStatusLabel.style.right = 28f;
        _previewStatusLabel.style.top = 18f;
        _previewStatusLabel.style.bottom = 18f;
        _previewStatusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        _previewStatusLabel.style.whiteSpace = WhiteSpace.Normal;
        _previewContainer.Add(_previewStatusLabel);

        _previewDebugLabel = CreateLabel(11, FontStyle.Bold, new Color(0.97f, 0.85f, 0.57f, 0.96f));
        _previewDebugLabel.style.position = Position.Absolute;
        _previewDebugLabel.style.right = 14f;
        _previewDebugLabel.style.top = 12f;
        _previewDebugLabel.style.display = DisplayStyle.None;
        _previewContainer.Add(_previewDebugLabel);
    }

    private void BuildFooter(VisualElement parent)
    {
        var footer = new VisualElement();
        footer.style.height = 56f;
        footer.style.backgroundColor = new Color(0.10f, 0.12f, 0.16f, 0.98f);
        footer.style.borderTopLeftRadius = 10f;
        footer.style.borderTopRightRadius = 10f;
        footer.style.borderBottomLeftRadius = 10f;
        footer.style.borderBottomRightRadius = 10f;
        footer.style.paddingLeft = 16f;
        footer.style.paddingRight = 16f;
        footer.style.paddingTop = 10f;
        footer.style.paddingBottom = 10f;
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.alignItems = Align.Center;
        footer.style.marginTop = 10f;
        parent.Add(footer);

        _footerPrimary = CreateLabel(15, FontStyle.Bold, Color.white);
        _footerSecondary = CreateLabel(12, FontStyle.Normal, new Color(0.72f, 0.77f, 0.84f, 0.94f));
        _footerPrimary.style.display = DisplayStyle.None;
        _footerSecondary.style.display = DisplayStyle.None;
        footer.Add(CreateSpacer());

        var actions = new VisualElement();
        actions.style.flexDirection = FlexDirection.Row;
        footer.Add(actions);

        _deleteButton = CreateButton(HistoryPanelText.Delete(), _delete, 130f, 36f);
        _replayButton = CreateButton(HistoryPanelText.Replay(), _replay, 140f, 36f);
        var closeButton = CreateButton(HistoryPanelText.Close(), _close, 96f, 36f);
        StyleButton(
            _deleteButton,
            new Color(0.40f, 0.24f, 0.20f, 0.98f),
            new Color(1f, 0.93f, 0.90f, 1f)
        );
        StyleButton(
            _replayButton,
            new Color(0.19f, 0.31f, 0.39f, 0.98f),
            new Color(0.88f, 0.95f, 1f, 1f)
        );
        StyleButton(
            closeButton,
            new Color(0.29f, 0.20f, 0.20f, 0.98f),
            new Color(0.98f, 0.92f, 0.90f, 1f)
        );
        actions.Add(_deleteButton);
        _replayButton.style.marginLeft = 10f;
        actions.Add(_replayButton);
        closeButton.style.marginLeft = 10f;
        actions.Add(closeButton);
    }

    private ListView CreateRunList()
    {
        var list = new ListView();
        list.style.flexGrow = 1f;
        list.style.flexShrink = 1f;
        list.style.minHeight = 0f;
        list.style.height = Length.Percent(100);
        list.selectionType = SelectionType.Single;
        list.fixedItemHeight = 98f;
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
        list.fixedItemHeight = 96f;
        list.makeItem = MakeBattleRow;
        list.bindItem = BindBattleRow;
        return list;
    }
}
