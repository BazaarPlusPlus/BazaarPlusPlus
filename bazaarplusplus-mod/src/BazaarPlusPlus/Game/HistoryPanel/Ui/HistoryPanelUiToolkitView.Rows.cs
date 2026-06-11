#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelUiToolkitView
{
    private VisualElement MakeRunRow()
    {
        var row = CreateRowShell();
        row.style.marginTop = UiSpacing.Xs;
        row.style.marginBottom = UiSpacing.Xs;
        UiStyle.BorderWidth(row.style, Borders.Thin);
        var accent = CreateAccentBar();
        row.Add(accent);
        var outcomeHost = new VisualElement();
        outcomeHost.style.width = Sizes.RowSideHostWidth;
        outcomeHost.style.flexShrink = 0f;
        outcomeHost.style.alignItems = Align.Center;
        outcomeHost.style.justifyContent = Justify.Center;
        row.Add(outcomeHost);
        var outcomeBubble = CreateDayBubble(outcomeHost);
        var content = CreateRowContent();
        content.style.justifyContent = Justify.Center;
        content.style.paddingLeft = UiSpacing.Xs;
        row.Add(content);

        var topRow = CreateRowTopRow();
        content.Add(topRow);
        var heroPill = CreateInlinePill(topRow, Sizes.InlinePillMinWidth);
        SetFixedPillWidth(heroPill, Sizes.RunHeroPillWidth);
        var rankPill = CreateInlinePill(topRow, Sizes.RunRankPillWidth);
        SetFixedPillWidth(rankPill, Sizes.RunRankPillWidth);
        var progressPill = CreateInlinePill(topRow, Sizes.RunProgressPillMinWidth);
        SetFixedPillWidth(progressPill, Sizes.RunProgressPillWidth);
        var statusPill = CreateInlinePill(topRow, Sizes.RunStatusPillMinWidth);
        SetFixedPillWidth(statusPill, Sizes.RunStatusPillWidth);
        topRow.Add(CreateSpacer());
        var timeLabel = CreateRowCornerLabel(topRow, Sizes.FontCorner);

        var statRow = CreateInfoChipRow(content, UiSpacing.Sm, UiSpacing.Sm);
        var healthChip = CreateInfoChip(
            statRow,
            HistoryPanelText.StatHealthShort(),
            Sizes.InfoChipMinWidth
        );
        SetEqualChipWidth(healthChip);
        var prestigeChip = CreateInfoChip(
            statRow,
            HistoryPanelText.StatPrestigeShort(),
            Sizes.InfoChipMinWidth
        );
        SetEqualChipWidth(prestigeChip);
        var levelChip = CreateInfoChip(
            statRow,
            HistoryPanelText.StatLevelShort(),
            Sizes.InfoChipMinWidth
        );
        SetEqualChipWidth(levelChip);
        var incomeChip = CreateInfoChip(
            statRow,
            HistoryPanelText.StatIncomeShort(),
            Sizes.InfoChipMinWidth
        );
        SetEqualChipWidth(incomeChip);
        var goldChip = CreateInfoChip(
            statRow,
            HistoryPanelText.StatGoldShort(),
            Sizes.InfoChipMinWidth
        );
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
        if (
            string.Equals(run.GameMode?.Trim(), "Ranked", System.StringComparison.OrdinalIgnoreCase)
        )
        {
            if (string.Equals(rank, "Legendary", System.StringComparison.OrdinalIgnoreCase))
            {
                ConfigurePill(
                    refs.RankPill,
                    HistoryPanelText.RankLabel(rank, run.PlayerRating),
                    Colors.RankLegendaryBackground,
                    Colors.White,
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
                    Colors.HistoryUnrankedBackground,
                    Colors.HistoryUnrankedText,
                    true
                );
            }
        }
        else
        {
            ConfigurePill(
                refs.RankPill,
                HistoryPanelText.Unranked(),
                Colors.HistoryUnrankedBackground,
                Colors.HistoryUnrankedText,
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
            Colors.HistoryProgressBackground,
            Colors.HistoryProgressText,
            true
        );
        ConfigureInfoChip(
            refs.HealthChip,
            HistoryPanelText.StatHealthShort(),
            run.MaxHealth?.ToString() ?? "--",
            Colors.HistoryHealthAccent
        );
        ConfigureInfoChip(
            refs.PrestigeChip,
            HistoryPanelText.StatPrestigeShort(),
            run.Prestige?.ToString() ?? "--",
            Colors.HistoryPrestigeAccent
        );
        ConfigureInfoChip(
            refs.LevelChip,
            HistoryPanelText.StatLevelShort(),
            run.Level?.ToString() ?? "--",
            Colors.HistoryLevelAccent
        );
        ConfigureInfoChip(
            refs.IncomeChip,
            HistoryPanelText.StatIncomeShort(),
            run.Income?.ToString() ?? "--",
            Colors.HistoryGoldAccent
        );
        ConfigureInfoChip(
            refs.GoldChip,
            HistoryPanelText.StatGoldShort(),
            run.Gold?.ToString() ?? "--",
            Colors.HistoryGoldAccent
        );
        ApplyRunRowState(refs, _runsList?.selectedIndex == index);
    }

    private VisualElement MakeBattleRow()
    {
        var row = CreateRowShell();
        row.style.marginTop = UiSpacing.Xs;
        row.style.marginBottom = UiSpacing.Xs;
        UiStyle.BorderWidth(row.style, Borders.Thin);
        var accent = CreateAccentBar();
        row.Add(accent);
        var dayHost = new VisualElement();
        dayHost.style.width = Sizes.RowSideHostWidth;
        dayHost.style.flexShrink = 0f;
        dayHost.style.alignItems = Align.Center;
        dayHost.style.justifyContent = Justify.Center;
        row.Add(dayHost);
        var dayBubble = CreateBattleDayBubble(dayHost);
        var content = CreateRowContent();
        content.style.paddingLeft = UiSpacing.Xs;
        content.style.paddingTop = UiSpacing.Md;
        content.style.paddingBottom = UiSpacing.Md;
        content.style.justifyContent = Justify.Center;
        row.Add(content);

        var playerRow = CreateInfoChipRow(content, UiSpacing.Sm, UiSpacing.None);
        var opponentRankPill = CreateInlinePill(playerRow, Sizes.BattleRankPillMinWidth);
        SetFixedPillWidth(opponentRankPill, Sizes.BattleRankPillWidth);
        var playerSummaryChip = CreateInfoChip(
            playerRow,
            HistoryPanelText.PlayerSideShort(),
            Sizes.InfoChipSummaryMinWidth
        );
        playerSummaryChip.style.marginRight = UiSpacing.None;
        playerSummaryChip.style.marginLeft = UiSpacing.Md;
        var playerSpacer = CreateSpacer();
        playerRow.Add(playerSpacer);
        var timeLabel = CreateRowCornerLabel(playerRow, Sizes.FontCorner);

        var opponentRow = CreateInfoChipRow(content, UiSpacing.Sm, UiSpacing.Sm);
        var opponentHeroPill = CreateInlinePill(opponentRow, Sizes.InlinePillMinWidth);
        SetFixedPillWidth(opponentHeroPill, Sizes.BattleRankPillWidth);
        var opponentSummaryChip = CreateInfoChip(
            opponentRow,
            HistoryPanelText.OpponentSideShort(),
            Sizes.InfoChipSummaryMinWidth
        );
        opponentSummaryChip.style.marginRight = UiSpacing.None;
        opponentSummaryChip.style.marginLeft = UiSpacing.Md;

        var eliminatedChip = CreateInlinePill(opponentRow, UiSpacing.None);
        eliminatedChip.style.marginLeft = UiSpacing.Lg;
        eliminatedChip.style.marginRight = UiSpacing.None;
        UiStyle.HorizontalPadding(eliminatedChip.style, UiSpacing.Lg);
        eliminatedChip.style.fontSize = Sizes.FontCorner;
        eliminatedChip.style.color = Colors.HistoryEliminatedText;
        eliminatedChip.style.backgroundColor = Colors.HistoryEliminatedBackground;
        UiStyle.Border(eliminatedChip.style, Borders.Thin, Colors.HistoryEliminatedBorder);
        eliminatedChip.style.display = DisplayStyle.None;

        var opponentName = CreateInlineText(
            opponentRow,
            Sizes.FontSmall,
            Colors.HistoryOpponentNameText
        );
        opponentName.style.marginLeft = UiSpacing.Lg;
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
            HistoryPanelText.BoardSummary(battle.PlayerHandItemCount, battle.PlayerSkillCount),
            Colors.HistoryPlayerAccent
        );

        BindHeroPill(refs.OpponentHeroPill, battle.OpponentHero);
        ConfigureInfoChip(
            refs.OpponentSummaryChip,
            HistoryPanelText.OpponentSideShort(),
            HistoryPanelText.BoardSummary(battle.OpponentHandItemCount, battle.OpponentSkillCount),
            Colors.HistoryOpponentAccent
        );
        var opponentName = battle.OpponentName ?? string.Empty;
        refs.OpponentName.text = StablePanelText.Compact(opponentName, 48);
        refs.OpponentName.tooltip = opponentName;
        refs.OpponentName.style.display = string.IsNullOrWhiteSpace(refs.OpponentName.text)
            ? DisplayStyle.None
            : DisplayStyle.Flex;

        var isEliminated = HistoryPanelFormatter.IsGhostOpponentEliminated(battle);
        refs.EliminatedChip.text = HistoryPanelText.GhostOpponentEliminatedShort();
        refs.EliminatedChip.style.display = isEliminated ? DisplayStyle.Flex : DisplayStyle.None;

        ApplyBattleRowState(refs, _battleList?.selectedIndex == index, battle);
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
