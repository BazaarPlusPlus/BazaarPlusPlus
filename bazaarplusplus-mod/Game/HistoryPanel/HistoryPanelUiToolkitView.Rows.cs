#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanelUiToolkitView
{
    private VisualElement MakeRunRow()
    {
        var row = CreateRowShell();
        row.style.marginTop = 4f;
        row.style.marginBottom = 4f;
        row.style.borderLeftWidth = 1f;
        row.style.borderRightWidth = 1f;
        row.style.borderTopWidth = 1f;
        row.style.borderBottomWidth = 1f;
        var accent = CreateAccentBar();
        row.Add(accent);
        var outcomeHost = new VisualElement();
        outcomeHost.style.width = 62f;
        outcomeHost.style.flexShrink = 0f;
        outcomeHost.style.alignItems = Align.Center;
        outcomeHost.style.justifyContent = Justify.Center;
        row.Add(outcomeHost);
        var outcomeBubble = CreateDayBubble(outcomeHost);
        var content = CreateRowContent();
        content.style.justifyContent = Justify.Center;
        content.style.paddingLeft = 4f;
        row.Add(content);

        var topRow = CreateRowTopRow();
        content.Add(topRow);
        var heroPill = CreateInlinePill(topRow, 64f);
        SetFixedPillWidth(heroPill, 60f);
        var rankPill = CreateInlinePill(topRow, 84f);
        SetFixedPillWidth(rankPill, 84f);
        var progressPill = CreateInlinePill(topRow, 72f);
        SetFixedPillWidth(progressPill, 50f);
        var statusPill = CreateInlinePill(topRow, 84f);
        SetFixedPillWidth(statusPill, 60f);
        topRow.Add(CreateSpacer());
        var timeLabel = CreateRowCornerLabel(topRow, 11);

        var statRow = CreateInfoChipRow(content, 6f, 6f);
        var healthChip = CreateInfoChip(statRow, HistoryPanelText.StatHealthShort(), 54f);
        SetEqualChipWidth(healthChip);
        var prestigeChip = CreateInfoChip(statRow, HistoryPanelText.StatPrestigeShort(), 54f);
        SetEqualChipWidth(prestigeChip);
        var levelChip = CreateInfoChip(statRow, HistoryPanelText.StatLevelShort(), 54f);
        SetEqualChipWidth(levelChip);
        var incomeChip = CreateInfoChip(statRow, HistoryPanelText.StatIncomeShort(), 54f);
        SetEqualChipWidth(incomeChip);
        var goldChip = CreateInfoChip(statRow, HistoryPanelText.StatGoldShort(), 54f);
        SetEqualChipWidth(goldChip, isLast: true);
        var refs = new RunRowRefs(
            row,
            accent,
            outcomeBubble,
            rankPill,
            heroPill,
            progressPill,
            statusPill,
            timeLabel,
            statRow,
            healthChip,
            prestigeChip,
            levelChip,
            incomeChip,
            goldChip
        );
        row.userData = refs;
        row.RegisterCallback<ClickEvent>(_ =>
        {
            if (!_suppressSelectionCallbacks && refs.Index >= 0)
                _selectRun(refs.Index);
        });
        return row;
    }

    private void BindRunRow(VisualElement element, int index)
    {
        if (
            _runsList?.itemsSource is not List<HistoryRunRecord> items
            || index < 0
            || index >= items.Count
        )
            return;

        var run = items[index];
        var refs = (RunRowRefs)element.userData;
        refs.Index = index;
        BindRunOutcomeBubble(refs.OutcomeBubble, run);
        var timing = new List<string>();
        var duration = HistoryPanelFormatter.FormatRunDuration(run);
        if (!string.IsNullOrWhiteSpace(duration))
            timing.Add(duration);
        timing.Add(HistoryPanelFormatter.FormatTimestamp(run.LastSeenAtUtc));
        refs.Time.text = string.Join(" · ", timing);

        var rank = HistoryPanelFormatter.NormalizeRank(run.PlayerRank);
        if (string.Equals(run.GameMode?.Trim(), "Ranked", System.StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(rank, "Legendary", System.StringComparison.OrdinalIgnoreCase))
            {
                ConfigurePill(
                    refs.RankPill,
                    HistoryPanelText.RankLabel(rank, run.PlayerRating),
                    ColorFromRgb(241, 54, 41),
                    Color.white,
                    true
                );
            }
            else if (!string.IsNullOrWhiteSpace(rank))
            {
                var palette = GetRankBadgePalette(rank);
                ConfigurePill(
                    refs.RankPill,
                    HistoryPanelText.RankLabel(rank),
                    palette.Background,
                    palette.Text,
                    true
                );
            }
            else
            {
                ConfigurePill(
                    refs.RankPill,
                    HistoryPanelText.Unranked(),
                    new Color(0.22f, 0.24f, 0.29f, 0.98f),
                    new Color(0.90f, 0.94f, 1f, 1f),
                    true
                );
            }
        }
        else
        {
            ConfigurePill(
                refs.RankPill,
                HistoryPanelText.Unranked(),
                new Color(0.22f, 0.24f, 0.29f, 0.98f),
                new Color(0.90f, 0.94f, 1f, 1f),
                true
            );
        }

        ConfigurePill(
            refs.HeroPill,
            GetHeroBadgeStyle(run.Hero).ShortCode,
            GetHeroBadgeStyle(run.Hero).Background,
            GetHeroBadgeStyle(run.Hero).Text,
            true
        );

        ConfigureStatusPill(refs.StatusPill, run.RawStatus);

        ConfigurePill(
            refs.ProgressPill,
            $"{(run.Victories ?? 0)}/{(run.FinalDay?.ToString() ?? "?")}",
            new Color(0.20f, 0.24f, 0.31f, 0.98f),
            new Color(0.89f, 0.94f, 1f, 1f),
            true
        );
        ConfigureInfoChip(
            refs.HealthChip,
            HistoryPanelText.StatHealthShort(),
            run.MaxHealth?.ToString() ?? "--",
            new Color(0.63f, 0.98f, 0.35f, 1f)
        );
        ConfigureInfoChip(
            refs.PrestigeChip,
            HistoryPanelText.StatPrestigeShort(),
            run.Prestige?.ToString() ?? "--",
            new Color(1f, 0.65f, 0.13f, 1f)
        );
        ConfigureInfoChip(
            refs.LevelChip,
            HistoryPanelText.StatLevelShort(),
            run.Level?.ToString() ?? "--",
            new Color(0.36f, 0.79f, 1f, 1f)
        );
        ConfigureInfoChip(
            refs.IncomeChip,
            HistoryPanelText.StatIncomeShort(),
            run.Income?.ToString() ?? "--",
            new Color(1f, 0.86f, 0.10f, 1f)
        );
        ConfigureInfoChip(
            refs.GoldChip,
            HistoryPanelText.StatGoldShort(),
            run.Gold?.ToString() ?? "--",
            new Color(1f, 0.86f, 0.10f, 1f)
        );
        ApplyRunRowState(refs, _runsList?.selectedIndex == index);
    }

    private VisualElement MakeBattleRow()
    {
        var row = CreateRowShell();
        row.style.marginTop = 4f;
        row.style.marginBottom = 4f;
        row.style.borderLeftWidth = 1f;
        row.style.borderRightWidth = 1f;
        row.style.borderTopWidth = 1f;
        row.style.borderBottomWidth = 1f;
        var accent = CreateAccentBar();
        row.Add(accent);
        var dayHost = new VisualElement();
        dayHost.style.width = 62f;
        dayHost.style.flexShrink = 0f;
        dayHost.style.alignItems = Align.Center;
        dayHost.style.justifyContent = Justify.Center;
        row.Add(dayHost);
        var dayBubble = CreateBattleDayBubble(dayHost);
        var content = CreateRowContent();
        content.style.paddingLeft = 4f;
        content.style.paddingTop = 8f;
        content.style.paddingBottom = 8f;
        content.style.justifyContent = Justify.Center;
        row.Add(content);

        var playerRow = CreateInfoChipRow(content, 6f, 0f);
        var opponentRankPill = CreateInlinePill(playerRow, 68f);
        SetFixedPillWidth(opponentRankPill, 80f);
        var playerSummaryChip = CreateInfoChip(playerRow, HistoryPanelText.PlayerSideShort(), 100f);
        playerSummaryChip.style.marginRight = 0f;
        playerSummaryChip.style.marginLeft = 8f;
        var playerSpacer = CreateSpacer();
        playerRow.Add(playerSpacer);
        var timeLabel = CreateRowCornerLabel(playerRow, 11);

        var opponentRow = CreateInfoChipRow(content, 6f, 6f);
        var opponentHeroPill = CreateInlinePill(opponentRow, 64f);
        SetFixedPillWidth(opponentHeroPill, 80f);
        var opponentSummaryChip = CreateInfoChip(
            opponentRow,
            HistoryPanelText.OpponentSideShort(),
            100f
        );
        opponentSummaryChip.style.marginRight = 0f;
        opponentSummaryChip.style.marginLeft = 8f;

        var eliminatedChip = CreateInlinePill(opponentRow, 0f);
        eliminatedChip.style.marginLeft = 10f;
        eliminatedChip.style.marginRight = 0f;
        eliminatedChip.style.paddingLeft = 10f;
        eliminatedChip.style.paddingRight = 10f;
        eliminatedChip.style.fontSize = 11;
        eliminatedChip.style.color = new Color(0.99f, 0.90f, 0.68f, 1f);
        eliminatedChip.style.backgroundColor = new Color(0.32f, 0.24f, 0.10f, 0.96f);
        eliminatedChip.style.borderTopWidth = 1f;
        eliminatedChip.style.borderRightWidth = 1f;
        eliminatedChip.style.borderBottomWidth = 1f;
        eliminatedChip.style.borderLeftWidth = 1f;
        var eliminatedBorder = new Color(0.94f, 0.70f, 0.28f, 0.55f);
        eliminatedChip.style.borderTopColor = eliminatedBorder;
        eliminatedChip.style.borderRightColor = eliminatedBorder;
        eliminatedChip.style.borderBottomColor = eliminatedBorder;
        eliminatedChip.style.borderLeftColor = eliminatedBorder;
        eliminatedChip.style.display = DisplayStyle.None;

        var opponentName = CreateInlineText(opponentRow, 12, new Color(0.76f, 0.80f, 0.87f, 0.92f));
        opponentName.style.marginLeft = 10f;
        opponentName.style.flexGrow = 1f;
        opponentName.style.unityTextAlign = TextAnchor.MiddleRight;

        var refs = new BattleRowRefs(
            row,
            accent,
            dayBubble,
            timeLabel,
            opponentRankPill,
            playerSummaryChip,
            opponentHeroPill,
            opponentSummaryChip,
            eliminatedChip,
            opponentName
        );
        row.userData = refs;
        row.RegisterCallback<ClickEvent>(_ =>
        {
            if (!_suppressSelectionCallbacks && refs.Index >= 0)
                _selectBattle(refs.Index);
        });
        return row;
    }

    private void BindBattleRow(VisualElement element, int index)
    {
        if (
            _battleList?.itemsSource is not List<HistoryBattleRecord> items
            || index < 0
            || index >= items.Count
        )
            return;

        var battle = items[index];
        var refs = (BattleRowRefs)element.userData;
        refs.Index = index;

        refs.DayBubble.text = battle.Day?.ToString() ?? "?";
        refs.Time.text = HistoryPanelFormatter.FormatTimestamp(battle.RecordedAtUtc);

        BindBattleRankPill(refs.OpponentRankPill, battle.OpponentRank, battle.OpponentRating);
        ConfigureInfoChip(
            refs.PlayerSummaryChip,
            HistoryPanelText.PlayerSideShort(),
            HistoryPanelText.BoardSummary(
                battle.PreviewData.PlayerBoard.ItemCards.Count,
                battle.PreviewData.PlayerBoard.SkillCards.Count
            ),
            new Color(0.44f, 0.76f, 1f, 1f)
        );

        BindHeroPill(refs.OpponentHeroPill, battle.OpponentHero);
        ConfigureInfoChip(
            refs.OpponentSummaryChip,
            HistoryPanelText.OpponentSideShort(),
            HistoryPanelText.BoardSummary(
                battle.PreviewData.OpponentBoard.ItemCards.Count,
                battle.PreviewData.OpponentBoard.SkillCards.Count
            ),
            new Color(0.96f, 0.77f, 0.39f, 1f)
        );
        refs.OpponentName.text = battle.OpponentName ?? string.Empty;
        refs.OpponentName.style.display = string.IsNullOrWhiteSpace(refs.OpponentName.text)
            ? DisplayStyle.None
            : DisplayStyle.Flex;

        var isEliminated = HistoryPanelFormatter.IsGhostOpponentEliminated(battle);
        refs.EliminatedChip.text = HistoryPanelText.GhostOpponentEliminatedShort();
        refs.EliminatedChip.style.display = isEliminated ? DisplayStyle.Flex : DisplayStyle.None;

        ApplyBattleRowState(refs, _battleList?.selectedIndex == index, battle);
    }

    private static void ApplyRunRowState(RunRowRefs refs, bool selected)
    {
        refs.Root.style.backgroundColor = selected
            ? new Color(0.17f, 0.24f, 0.32f, 0.99f)
            : new Color(0.11f, 0.14f, 0.18f, 0.98f);
        refs.Accent.style.backgroundColor = selected
            ? new Color(0.46f, 0.70f, 0.92f, 0.94f)
            : new Color(0.24f, 0.31f, 0.39f, 0.96f);
        var borderColor = selected
            ? new Color(0.37f, 0.57f, 0.79f, 0.56f)
            : new Color(0.28f, 0.35f, 0.45f, 0.40f);
        refs.Root.style.borderLeftColor = borderColor;
        refs.Root.style.borderRightColor = borderColor;
        refs.Root.style.borderTopColor = borderColor;
        refs.Root.style.borderBottomColor = borderColor;
        refs.OutcomeBubble.style.borderLeftColor = borderColor;
        refs.OutcomeBubble.style.borderRightColor = borderColor;
        refs.OutcomeBubble.style.borderTopColor = borderColor;
        refs.OutcomeBubble.style.borderBottomColor = borderColor;
        refs.OutcomeBubble.style.opacity = selected ? 1f : 0.96f;
    }

    private static void ApplyBattleRowState(
        BattleRowRefs refs,
        bool selected,
        HistoryBattleRecord battle
    )
    {
        var isWin = HistoryPanelFormatter.IsBattleWin(battle);
        var isLoss = HistoryPanelFormatter.IsBattleLoss(battle);
        var isEliminated = HistoryPanelFormatter.IsGhostOpponentEliminated(battle);

        refs.Root.style.backgroundColor = selected
            ? isEliminated
                ? new Color(0.22f, 0.18f, 0.10f, 0.99f)
                : isWin
                    ? new Color(0.13f, 0.23f, 0.22f, 0.99f)
                    : isLoss
                        ? new Color(0.24f, 0.18f, 0.16f, 0.99f)
                        : new Color(0.18f, 0.24f, 0.31f, 0.99f)
            : isEliminated
                ? new Color(0.18f, 0.14f, 0.08f, 0.98f)
                : isWin
                    ? new Color(0.10f, 0.15f, 0.16f, 0.98f)
                    : isLoss
                        ? new Color(0.15f, 0.13f, 0.15f, 0.98f)
                        : new Color(0.13f, 0.15f, 0.18f, 0.98f);

        refs.Accent.style.backgroundColor =
            isEliminated ? new Color(0.94f, 0.70f, 0.28f, 0.95f)
            : isWin ? new Color(0.23f, 0.54f, 0.47f, 0.95f)
            : isLoss ? new Color(0.63f, 0.36f, 0.24f, 0.95f)
            : new Color(0.34f, 0.47f, 0.64f, 0.95f);
        var borderColor =
            isEliminated ? new Color(0.62f, 0.46f, 0.18f, 0.50f)
            : isWin ? new Color(0.22f, 0.44f, 0.40f, 0.42f)
            : isLoss ? new Color(0.44f, 0.27f, 0.20f, 0.42f)
            : new Color(0.24f, 0.31f, 0.41f, 0.42f);
        refs.Root.style.borderLeftColor = borderColor;
        refs.Root.style.borderRightColor = borderColor;
        refs.Root.style.borderTopColor = borderColor;
        refs.Root.style.borderBottomColor = borderColor;
        refs.DayBubble.style.backgroundColor =
            isEliminated ? new Color(0.24f, 0.18f, 0.08f, 0.98f)
            : isWin ? new Color(0.13f, 0.28f, 0.23f, 0.98f)
            : isLoss ? new Color(0.33f, 0.20f, 0.15f, 0.98f)
            : new Color(0.18f, 0.23f, 0.31f, 0.98f);
        refs.DayBubble.style.borderLeftColor = borderColor;
        refs.DayBubble.style.borderRightColor = borderColor;
        refs.DayBubble.style.borderTopColor = borderColor;
        refs.DayBubble.style.borderBottomColor = borderColor;
    }

    private sealed class RowRefs
    {
        public RowRefs(
            VisualElement root,
            VisualElement accent,
            Label title,
            Label pill,
            Label meta,
            Label detail
        )
        {
            Root = root;
            Accent = accent;
            Title = title;
            Pill = pill;
            Meta = meta;
            Detail = detail;
            Index = -1;
        }

        public VisualElement Root { get; }

        public VisualElement Accent { get; }

        public Label Title { get; }

        public Label Pill { get; }

        public Label Meta { get; }

        public Label Detail { get; }

        public int Index { get; set; }
    }

    private sealed class RunRowRefs
    {
        public RunRowRefs(
            VisualElement root,
            VisualElement accent,
            Label outcomeBubble,
            Label rankPill,
            Label heroPill,
            Label progressPill,
            Label statusPill,
            Label time,
            VisualElement statRow,
            Label healthChip,
            Label prestigeChip,
            Label levelChip,
            Label incomeChip,
            Label goldChip
        )
        {
            Root = root;
            Accent = accent;
            OutcomeBubble = outcomeBubble;
            RankPill = rankPill;
            HeroPill = heroPill;
            ProgressPill = progressPill;
            StatusPill = statusPill;
            Time = time;
            StatRow = statRow;
            HealthChip = healthChip;
            PrestigeChip = prestigeChip;
            LevelChip = levelChip;
            IncomeChip = incomeChip;
            GoldChip = goldChip;
            Index = -1;
        }

        public VisualElement Root { get; }

        public VisualElement Accent { get; }

        public Label OutcomeBubble { get; }

        public Label RankPill { get; }

        public Label HeroPill { get; }

        public Label ProgressPill { get; }

        public Label StatusPill { get; }

        public Label Time { get; }

        public VisualElement StatRow { get; }

        public Label HealthChip { get; }

        public Label PrestigeChip { get; }

        public Label LevelChip { get; }

        public Label IncomeChip { get; }

        public Label GoldChip { get; }

        public int Index { get; set; }
    }

    private sealed class BattleRowRefs
    {
        public BattleRowRefs(
            VisualElement root,
            VisualElement accent,
            Label dayBubble,
            Label time,
            Label opponentRankPill,
            Label playerSummaryChip,
            Label opponentHeroPill,
            Label opponentSummaryChip,
            Label eliminatedChip,
            Label opponentName
        )
        {
            Root = root;
            Accent = accent;
            DayBubble = dayBubble;
            Time = time;
            OpponentRankPill = opponentRankPill;
            PlayerSummaryChip = playerSummaryChip;
            OpponentHeroPill = opponentHeroPill;
            OpponentSummaryChip = opponentSummaryChip;
            EliminatedChip = eliminatedChip;
            OpponentName = opponentName;
            Index = -1;
        }

        public VisualElement Root { get; }

        public VisualElement Accent { get; }

        public Label DayBubble { get; }

        public Label Time { get; }

        public Label OpponentRankPill { get; }

        public Label PlayerSummaryChip { get; }

        public Label OpponentHeroPill { get; }

        public Label OpponentSummaryChip { get; }

        public Label EliminatedChip { get; }

        public Label OpponentName { get; }

        public int Index { get; set; }
    }
}
