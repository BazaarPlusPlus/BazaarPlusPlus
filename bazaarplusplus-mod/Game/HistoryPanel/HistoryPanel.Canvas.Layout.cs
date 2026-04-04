#nullable enable
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private void BuildHeader()
    {
        var header = CreateRect("Header", _panelRoot!);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.anchoredPosition = new Vector2(0f, -20f);
        header.sizeDelta = new Vector2(-48f, 112f);

        var headerLayout = CreateVerticalGroup(
            "HeaderLayout",
            header,
            6f,
            CreatePadding(0f, 0f, 0f, 0f),
            TextAnchor.UpperLeft,
            true,
            true,
            true,
            false
        );
        StretchToParent(headerLayout, 0f, 0f, 0f, 0f);

        var title = CreateText("Title", headerLayout, 28, FontStyle.Bold, TextAnchor.UpperLeft);
        title.text = "Game History";
        title.color = new Color(0.97f, 0.85f, 0.57f, 1f);
        ConfigureLayoutElement(title.gameObject, preferredHeight: 32f, minHeight: 32f);

        var subtitle = CreateText(
            "Subtitle",
            headerLayout,
            14,
            FontStyle.Normal,
            TextAnchor.UpperLeft
        );
        subtitle.text =
            "Review your game history and replay any battle you want.";
        subtitle.color = new Color(0.82f, 0.86f, 0.91f, 0.94f);
        subtitle.textWrappingMode = TextWrappingModes.Normal;
        subtitle.overflowMode = TextOverflowModes.Ellipsis;
        ConfigureLayoutElement(subtitle.gameObject, preferredHeight: 28f, minHeight: 28f);

        var chipsRow = CreateHorizontalGroup(
            "ChipsRow",
            headerLayout,
            8f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(chipsRow.gameObject, preferredHeight: 34f, minHeight: 34f);

        _countChipText = CreateChip(chipsRow, 86f);
        _battleChipText = CreateChip(chipsRow, 96f);
        _databaseChipText = CreateChip(chipsRow, 110f);
        CreateFlexibleSpacer("Spacer", chipsRow);
        (_runsTabButton, _runsTabButtonBackground, _runsTabButtonLabel) = CreateStyledButton(
            "RunsTabButton",
            chipsRow,
            "Runs",
            92f,
            32f
        );
        _runsTabButton.onClick.AddListener(() => SetSectionMode(HistorySectionMode.Runs));
        (_ghostTabButton, _ghostTabButtonBackground, _ghostTabButtonLabel) = CreateStyledButton(
            "GhostTabButton",
            chipsRow,
            "Ghost",
            92f,
            32f
        );
        _ghostTabButton.onClick.AddListener(() => SetSectionMode(HistorySectionMode.Ghost));
        (_syncGhostButton, _syncGhostButtonBackground, _syncGhostButtonLabel) = CreateStyledButton(
            "SyncGhostButton",
            chipsRow,
            "Sync Ghost",
            114f,
            32f
        );
        _syncGhostButton.onClick.AddListener(TrySyncGhostBattles);
        (_dynamicPreviewButton, _dynamicPreviewButtonBackground, _dynamicPreviewButtonLabel) =
            CreateStyledButton(
                "DynamicPreviewButton",
                chipsRow,
                GetDynamicPreviewButtonLabel(false),
                120f,
                32f
            );
        _dynamicPreviewButton.onClick.AddListener(ToggleDynamicPreviewFromUi);
        CreateActionButton("CloseButton", chipsRow, "Close", 86f, () => SetHistoryVisible(false));

        _statusText = CreateText(
            "Status",
            headerLayout,
            12,
            FontStyle.Normal,
            TextAnchor.UpperLeft
        );
        _statusText.color = new Color(0.93f, 0.79f, 0.51f, 0.98f);
        _statusText.textWrappingMode = TextWrappingModes.NoWrap;
        _statusText.overflowMode = TextOverflowModes.Ellipsis;
        _statusText.gameObject.SetActive(false);
        ConfigureLayoutElement(_statusText.gameObject, preferredHeight: 18f, minHeight: 18f);
    }

    private void BuildContent()
    {
        var content = CreateRect("Content", _panelRoot!);
        content.anchorMin = new Vector2(0f, 0f);
        content.anchorMax = new Vector2(1f, 1f);
        content.offsetMin = new Vector2(24f, 96f);
        content.offsetMax = new Vector2(-24f, -164f);

        var outerLayout = CreateVerticalGroup(
            "OuterLayout",
            content,
            10f,
            null,
            TextAnchor.UpperLeft,
            true,
            true,
            true,
            false
        );
        StretchToParent(outerLayout, 0f, 0f, 0f, 0f);

        var columnsRow = CreateHorizontalGroup(
            "ColumnsRow",
            outerLayout,
            18f,
            null,
            TextAnchor.UpperLeft,
            true,
            true,
            false,
            true
        );
        ConfigureLayoutElement(columnsRow.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);

        _runSectionPanel = CreateSectionPanel(columnsRow, "RunsPanel");
        ConfigureLayoutElement(
            _runSectionPanel.gameObject,
            preferredWidth: ListColumnWidth,
            minWidth: ListColumnWidth,
            preferredHeight: 0f,
            flexibleHeight: 1f
        );
        var leftLayout = CreateVerticalGroup(
            "RunsLayout",
            _runSectionPanel,
            10f,
            CreatePadding(14f, 14f, 14f, 14f),
            TextAnchor.UpperLeft,
            true,
            true,
            true,
            false
        );
        StretchToParent(leftLayout, 0f, 0f, 0f, 0f);
        BuildSectionHeader(
            leftLayout,
            "Runs",
            "Choose one run to see its recorded battles.",
            out _runSectionTitle,
            out _
        );
        _runListContent = CreateScrollSection(leftLayout, "RunScroll");

        var right = CreateSectionPanel(columnsRow, "BattlesPanel");
        ConfigureLayoutElement(
            right.gameObject,
            flexibleWidth: 1f,
            preferredHeight: 0f,
            flexibleHeight: 1f
        );
        BuildGhostBattleSection(right);
        BuildRunsBattleSection(right);

        BuildPreviewSection(outerLayout);
    }

    private void BuildGhostBattleSection(RectTransform parent)
    {
        var layout = CreateVerticalGroup(
            "GhostBattleLayout",
            parent,
            2f,
            CreatePadding(14f, 14f, 4f, 4f),
            TextAnchor.UpperLeft,
            true,
            true,
            true,
            false
        );
        StretchToParent(layout, 0f, 0f, 0f, 0f);
        _ghostModeRoot = layout;

        var filterRow = CreateHorizontalGroup(
            "GhostFilterRow",
            layout,
            8f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(
            filterRow.gameObject,
            preferredHeight: GhostFilterButtonHeight,
            minHeight: GhostFilterButtonHeight
        );
        (_ghostFilterAllButton, _ghostFilterAllButtonBackground, _ghostFilterAllButtonLabel) =
            CreateStyledButton(
                "GhostFilterAllButton",
                filterRow,
                "All",
                70f,
                GhostFilterButtonHeight
            );
        ConfigureCompactGhostFilterLabel(_ghostFilterAllButtonLabel);
        _ghostFilterAllButton.onClick.AddListener(() =>
            SetGhostBattleFilter(GhostBattleFilter.All)
        );
        (_ghostFilterIWonButton, _ghostFilterIWonButtonBackground, _ghostFilterIWonButtonLabel) =
            CreateStyledButton(
                "GhostFilterIWonButton",
                filterRow,
                "I Won",
                78f,
                GhostFilterButtonHeight
            );
        ConfigureCompactGhostFilterLabel(_ghostFilterIWonButtonLabel);
        _ghostFilterIWonButton.onClick.AddListener(() =>
            SetGhostBattleFilter(GhostBattleFilter.IWon)
        );
        (_ghostFilterILostButton, _ghostFilterILostButtonBackground, _ghostFilterILostButtonLabel) =
            CreateStyledButton(
                "GhostFilterILostButton",
                filterRow,
                "I Lost",
                78f,
                GhostFilterButtonHeight
            );
        ConfigureCompactGhostFilterLabel(_ghostFilterILostButtonLabel);
        _ghostFilterILostButton.onClick.AddListener(() =>
            SetGhostBattleFilter(GhostBattleFilter.ILost)
        );
        CreateFlexibleSpacer("GhostFilterSpacer", filterRow);

        _ghostBattleListContent = CreateScrollSection(layout, "GhostBattleScroll");
    }

    private void BuildRunsBattleSection(RectTransform parent)
    {
        var layout = CreateVerticalGroup(
            "RunsBattleLayout",
            parent,
            10f,
            CreatePadding(14f, 14f, 14f, 14f),
            TextAnchor.UpperLeft,
            true,
            true,
            true,
            false
        );
        StretchToParent(layout, 0f, 0f, 0f, 0f);
        _runsModeRoot = layout;

        BuildSectionHeader(layout, "Battles", string.Empty, out _, out _runsBattleSectionSubtitle);
        _runsBattleListContent = CreateScrollSection(layout, "RunsBattleScroll");
    }

    private void BuildFooter()
    {
        var footer = CreateRect("Footer", _panelRoot!);
        footer.anchorMin = new Vector2(0f, 0f);
        footer.anchorMax = new Vector2(1f, 0f);
        footer.pivot = new Vector2(0.5f, 0f);
        footer.anchoredPosition = new Vector2(0f, 20f);
        footer.sizeDelta = new Vector2(-48f, 68f);
        AddImage(footer.gameObject, new Color(0.10f, 0.12f, 0.16f, 0.98f));

        var divider = CreateRect("Divider", footer);
        divider.anchorMin = new Vector2(0f, 1f);
        divider.anchorMax = new Vector2(1f, 1f);
        divider.pivot = new Vector2(0.5f, 1f);
        divider.sizeDelta = new Vector2(0f, 1f);
        divider.gameObject.AddComponent<Image>().color = new Color(0.76f, 0.65f, 0.36f, 0.28f);

        var footerLayout = CreateHorizontalGroup(
            "FooterLayout",
            footer,
            12f,
            CreatePadding(16f, 16f, 8f, 8f),
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        StretchToParent(footerLayout, 0f, 0f, 0f, 0f);

        var textArea = CreateVerticalGroup(
            "TextArea",
            footerLayout,
            2f,
            null,
            TextAnchor.UpperLeft,
            true,
            false,
            true,
            false
        );
        ConfigureLayoutElement(textArea.gameObject, flexibleWidth: 1f);

        _footerPrimaryText = CreateText(
            "Primary",
            textArea,
            15,
            FontStyle.Bold,
            TextAnchor.UpperLeft
        );
        _footerPrimaryText.color = Color.white;
        _footerPrimaryText.textWrappingMode = TextWrappingModes.NoWrap;
        _footerPrimaryText.overflowMode = TextOverflowModes.Ellipsis;
        ConfigureLayoutElement(_footerPrimaryText.gameObject, preferredHeight: 22f, minHeight: 22f);

        _footerSecondaryText = CreateText(
            "Secondary",
            textArea,
            12,
            FontStyle.Normal,
            TextAnchor.UpperLeft
        );
        _footerSecondaryText.color = new Color(0.72f, 0.77f, 0.84f, 0.94f);
        _footerSecondaryText.textWrappingMode = TextWrappingModes.NoWrap;
        _footerSecondaryText.overflowMode = TextOverflowModes.Ellipsis;
        ConfigureLayoutElement(
            _footerSecondaryText.gameObject,
            preferredHeight: 22f,
            minHeight: 22f
        );

        var actions = CreateHorizontalGroup(
            "Actions",
            footerLayout,
            10f,
            null,
            TextAnchor.MiddleRight,
            false,
            true,
            false,
            false
        );
        ConfigureLayoutElement(actions.gameObject, preferredWidth: 390f, minWidth: 390f);

        (_deleteRunButton, _deleteRunButtonBackground, _deleteRunButtonLabel) = CreateStyledButton(
            "DeleteRunButton",
            actions,
            GetDeleteRunButtonLabel(false),
            130f,
            36f
        );
        _deleteRunButton.onClick.AddListener(TryDeleteSelectedRun);

        (_replayButton, _replayButtonBackground, _replayButtonLabel) = CreateStyledButton(
            "ReplayButton",
            actions,
            "Replay",
            120f,
            36f
        );
        _replayButton.onClick.AddListener(TryReplaySelectedBattle);
        CreateActionButton(
            "FooterCloseButton",
            actions,
            "Close",
            120f,
            () => SetHistoryVisible(false)
        );
    }
}
