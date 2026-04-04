#nullable enable
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private void RefreshListsIfNeeded()
    {
        RefreshRunListIfNeeded();
        RefreshBattleListIfNeeded();
    }

    private void RefreshRunListIfNeeded()
    {
        var signature = BuildRunListSignature();
        if (!string.Equals(_lastRenderedRunListSignature, signature, StringComparison.Ordinal))
        {
            RebuildRunList();
            _lastRenderedRunListSignature = signature;
            _lastRenderedRunSelectionIndex = _selectedRunIndex;
            return;
        }

        if (_lastRenderedRunSelectionIndex == _selectedRunIndex)
            return;

        UpdateRunItemSelectionStates();
        _lastRenderedRunSelectionIndex = _selectedRunIndex;
    }

    private void RefreshBattleListIfNeeded()
    {
        var signature = BuildBattleListSignature();
        if (
            _lastRenderedBattleSectionMode != _sectionMode
            || !string.Equals(_lastRenderedBattleListSignature, signature, StringComparison.Ordinal)
        )
        {
            RebuildBattleList();
            _lastRenderedBattleSectionMode = _sectionMode;
            _lastRenderedBattleListSignature = signature;
            _lastRenderedBattleSelectionIndex = GetCurrentBattleSelectionIndex();
            return;
        }

        var selectedIndex = GetCurrentBattleSelectionIndex();
        if (_lastRenderedBattleSelectionIndex == selectedIndex)
            return;

        UpdateBattleItemSelectionStates();
        _lastRenderedBattleSelectionIndex = selectedIndex;
    }

    private string BuildRunListSignature()
    {
        if (_runs.Count == 0)
            return "runs:empty";

        return "runs:"
            + string.Join(
                "|",
                _runs.ConvertAll(run =>
                    string.Join(
                        "~",
                        run.RunId,
                        run.Hero ?? string.Empty,
                        run.RawStatus ?? string.Empty,
                        run.GameMode ?? string.Empty,
                        run.LastSeenAtUtc.ToUnixTimeSeconds(),
                        run.FinalDay?.ToString() ?? string.Empty,
                        run.BattleCount,
                        run.Victories?.ToString() ?? string.Empty,
                        run.Losses?.ToString() ?? string.Empty,
                        run.PlayerRank ?? string.Empty,
                        run.PlayerRating?.ToString() ?? string.Empty,
                        run.MaxHealth?.ToString() ?? string.Empty,
                        run.Prestige?.ToString() ?? string.Empty,
                        run.Level?.ToString() ?? string.Empty,
                        run.Income?.ToString() ?? string.Empty,
                        run.Gold?.ToString() ?? string.Empty,
                        run.StartedAtUtc.ToUnixTimeSeconds(),
                        run.EndedAtUtc?.ToUnixTimeSeconds().ToString() ?? string.Empty
                    )
                )
            );
    }

    private string BuildBattleListSignature()
    {
        var visibleBattles =
            _sectionMode == HistorySectionMode.Ghost
                ? FilteredGhostBattles
                : (IReadOnlyList<HistoryBattleRecord>)_battles;
        if (visibleBattles.Count == 0)
            return $"{_sectionMode}:empty:{SelectedRun?.RunId ?? string.Empty}";

        var parts = new List<string>(visibleBattles.Count);
        foreach (var battle in visibleBattles)
        {
            parts.Add(
                string.Join(
                    "~",
                    battle.BattleId,
                    battle.Result ?? string.Empty,
                    battle.Day?.ToString() ?? string.Empty,
                    battle.OpponentName ?? string.Empty,
                    battle.PlayerHero ?? string.Empty,
                    battle.OpponentHero ?? string.Empty,
                    battle.OpponentRank ?? string.Empty,
                    battle.OpponentRating?.ToString() ?? string.Empty,
                    battle.PlayerLevel?.ToString() ?? string.Empty,
                    battle.OpponentLevel?.ToString() ?? string.Empty,
                    battle.RecordedAtUtc.ToUnixTimeSeconds(),
                    battle.SnapshotSummary ?? string.Empty,
                    battle.ReplayDownloaded,
                    battle.ReplayAvailable
                )
            );
        }

        return $"{_sectionMode}:{SelectedRun?.RunId ?? string.Empty}:{string.Join("|", parts)}";
    }

    private int GetCurrentBattleSelectionIndex()
    {
        return _sectionMode == HistorySectionMode.Ghost
            ? _selectedGhostBattleIndex
            : _selectedBattleIndex;
    }

    private void UpdateRunItemSelectionStates()
    {
        foreach (var itemView in _runItemViews)
        {
            if (itemView.Background == null)
                continue;

            ApplyItemState(
                itemView.Background,
                itemView.Index == _selectedRunIndex,
                new Color(0.11f, 0.14f, 0.18f, 0.98f),
                new Color(0.17f, 0.24f, 0.32f, 0.99f)
            );
        }
    }

    private void UpdateBattleItemSelectionStates()
    {
        var visibleBattles =
            _sectionMode == HistorySectionMode.Ghost
                ? FilteredGhostBattles
                : (IReadOnlyList<HistoryBattleRecord>)_battles;
        var selectedIndex = GetCurrentBattleSelectionIndex();
        foreach (var itemView in _battleItemViews)
        {
            if (itemView.Background == null)
                continue;
            if (itemView.Index < 0 || itemView.Index >= visibleBattles.Count)
                continue;

            var palette = GetBattlePalette(visibleBattles[itemView.Index]);
            ApplyItemState(
                itemView.Background,
                itemView.Index == selectedIndex,
                palette.Normal,
                palette.Selected
            );
        }
    }

    private void RebuildRunList()
    {
        if (_runListContent == null)
            return;

        ClearContainer(_runListContent, _runItemViews);

        if (_runs.Count == 0)
        {
            CreatePlaceholder(_runListContent, "No runs found yet.");
            _lastRenderedRunSelectionIndex = -1;
            return;
        }

        for (var i = 0; i < _runs.Count; i++)
            _runItemViews.Add(CreateRunItem(_runListContent, i, _runs[i], i == _selectedRunIndex));
    }

    private void RebuildBattleList()
    {
        if (_sectionMode == HistorySectionMode.Ghost)
            RebuildGhostBattleList();
        else
            RebuildRunsBattleList();
    }

    private void RebuildGhostBattleList()
    {
        if (_ghostBattleListContent == null)
            return;

        ClearContainer(_ghostBattleListContent, _battleItemViews);

        if (FilteredGhostBattles.Count == 0)
        {
            CreatePlaceholder(_ghostBattleListContent, "No ghost battles synced yet.");
            _lastRenderedBattleSelectionIndex = -1;
            return;
        }

        for (var i = 0; i < FilteredGhostBattles.Count; i++)
            _battleItemViews.Add(
                CreateBattleItem(
                    _ghostBattleListContent,
                    i,
                    FilteredGhostBattles[i],
                    i == _selectedGhostBattleIndex
                )
            );
    }

    private void RebuildRunsBattleList()
    {
        if (_runsBattleListContent == null)
            return;

        ClearContainer(_runsBattleListContent, _battleItemViews);

        if (SelectedRun == null)
        {
            CreatePlaceholder(_runsBattleListContent, "Select a run first.");
            _lastRenderedBattleSelectionIndex = -1;
            return;
        }

        if (_battles.Count == 0)
        {
            CreatePlaceholder(_runsBattleListContent, "No recorded battles for this run.");
            _lastRenderedBattleSelectionIndex = -1;
            return;
        }

        for (var i = 0; i < _battles.Count; i++)
            _battleItemViews.Add(
                CreateBattleItem(_runsBattleListContent, i, _battles[i], i == _selectedBattleIndex)
            );
    }

    private ListItemView CreateRunItem(
        Transform parent,
        int index,
        HistoryRunRecord run,
        bool selected
    )
    {
        var (button, background) = CreateCardButtonShell($"RunItem_{index}", parent, 130f);
        button.onClick.AddListener(() => SelectRun(index));

        var (_, body) = BuildCardShell(
            button.transform,
            selected
                ? new Color(0.46f, 0.70f, 0.92f, 0.94f)
                : new Color(0.24f, 0.31f, 0.39f, 0.96f),
            4f,
            CreatePadding(12f, 12f, 10f, 10f)
        );

        var topRow = CreateHorizontalGroup(
            "TopRow",
            body,
            8f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(topRow.gameObject, preferredHeight: 22f, minHeight: 22f);

        var pillRow = CreateHorizontalGroup(
            "PillRow",
            topRow,
            5f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(pillRow.gameObject, flexibleWidth: 1f);

        var runHeroStyle = GetHeroBadgeStyle(run.Hero);
        AddPill(
            pillRow,
            "Hero",
            runHeroStyle.ShortCode,
            runHeroStyle.Background,
            runHeroStyle.Text,
            60f
        );
        BuildRunRankBadge(pillRow, run);
        var achievement = HistoryPanelFormatter.FormatRunAchievement(run);
        if (!string.IsNullOrWhiteSpace(achievement))
        {
            AddPill(
                pillRow,
                "Achievement",
                achievement,
                GetRunAchievementBackground(achievement),
                GetRunAchievementText(achievement),
                72f
            );
        }

        var time = CreateText("Time", topRow, 11, FontStyle.Normal, TextAnchor.UpperRight);
        time.text = HistoryPanelFormatter.FormatTimestamp(run.LastSeenAtUtc);
        time.color = new Color(0.72f, 0.78f, 0.85f, 0.9f);
        time.textWrappingMode = TextWrappingModes.NoWrap;
        time.overflowMode = TextOverflowModes.Ellipsis;
        ConfigureLayoutElement(
            time.gameObject,
            preferredWidth: 96f,
            minWidth: 84f,
            preferredHeight: 16f
        );

        var metaParts = new List<string>
        {
            HistoryPanelFormatter.FormatDayOnly(run.FinalDay),
            $"{run.BattleCount} battles",
        };
        if (run.Victories.HasValue)
            metaParts.Add($"{run.Victories.Value} wins");
        var duration = HistoryPanelFormatter.FormatRunDuration(run);
        if (!string.IsNullOrWhiteSpace(duration))
            metaParts.Add(duration);

        AddDetailLine(
            body,
            string.Join("  |  ", metaParts),
            13,
            FontStyle.Normal,
            new Color(0.82f, 0.86f, 0.92f, 0.96f)
        );
        BuildRunStatStrip(body, run);

        ApplyItemState(
            background,
            selected,
            new Color(0.11f, 0.14f, 0.18f, 0.98f),
            new Color(0.17f, 0.24f, 0.32f, 0.99f)
        );
        return new ListItemView { Index = index, Background = background };
    }

    private ListItemView CreateBattleItem(
        Transform parent,
        int index,
        HistoryBattleRecord battle,
        bool selected
    )
    {
        var isGhostBattle = battle.Source == HistoryBattleSource.Ghost;
        var palette = GetBattlePalette(battle);
        var (button, background) = CreateCardButtonShell($"BattleItem_{index}", parent, 88f);
        button.onClick.AddListener(() => SelectBattle(index));

        var (_, body) = BuildCardShell(
            button.transform,
            palette.Accent,
            3f,
            CreatePadding(12f, 12f, 8f, 8f)
        );

        var topRow = CreateHorizontalGroup(
            "TopRow",
            body,
            8f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(topRow.gameObject, preferredHeight: 22f, minHeight: 22f);

        var pillRow = CreateHorizontalGroup(
            "PillRow",
            topRow,
            6f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(pillRow.gameObject, flexibleWidth: 1f);

        AddPill(
            pillRow,
            "Day",
            HistoryPanelFormatter.FormatDayOnly(battle.Day),
            new Color(0.18f, 0.21f, 0.27f, 0.94f),
            new Color(0.92f, 0.95f, 1f, 1f),
            72f
        );
        var playerHero = HistoryPanelFormatter.FormatOpponentHero(battle.PlayerHero);
        if (isGhostBattle && !string.IsNullOrWhiteSpace(playerHero))
        {
            var playerHeroStyle = GetHeroBadgeStyle(playerHero);
            AddPill(
                pillRow,
                "PlayerHero",
                $"YOU {playerHeroStyle.ShortCode}",
                playerHeroStyle.Background,
                playerHeroStyle.Text,
                88f
            );
        }
        var opponentHero = HistoryPanelFormatter.FormatOpponentHero(battle.OpponentHero);
        if (!string.IsNullOrWhiteSpace(opponentHero))
        {
            var opponentHeroStyle = GetHeroBadgeStyle(opponentHero);
            AddPill(
                pillRow,
                "OpponentHero",
                opponentHeroStyle.ShortCode,
                opponentHeroStyle.Background,
                opponentHeroStyle.Text,
                60f
            );
        }

        var time = CreateText("Time", topRow, 11, FontStyle.Normal, TextAnchor.UpperRight);
        time.text = HistoryPanelFormatter.FormatTimestamp(battle.RecordedAtUtc);
        time.color = new Color(0.72f, 0.78f, 0.85f, 0.9f);
        time.textWrappingMode = TextWrappingModes.NoWrap;
        time.overflowMode = TextOverflowModes.Ellipsis;
        ConfigureLayoutElement(
            time.gameObject,
            preferredWidth: 120f,
            minWidth: 80f,
            preferredHeight: 16f
        );

        BuildBattleNameRow(body, battle);
        var participantSummary = isGhostBattle ? BuildBattleParticipantSummary(battle) : null;
        if (!string.IsNullOrWhiteSpace(participantSummary))
        {
            AddDetailLine(
                body,
                participantSummary,
                12,
                FontStyle.Normal,
                new Color(0.82f, 0.87f, 0.93f, 0.95f)
            );
        }
        AddDetailLine(
            body,
            $"ID: {ShortenBattleId(battle.BattleId)}",
            11,
            FontStyle.Normal,
            new Color(0.70f, 0.75f, 0.83f, 0.90f)
        );
        if (!string.IsNullOrWhiteSpace(battle.SnapshotSummary))
        {
            AddDetailLine(
                body,
                battle.SnapshotSummary,
                12,
                FontStyle.Normal,
                new Color(0.74f, 0.80f, 0.87f, 0.95f)
            );
        }

        ApplyItemState(background, selected, palette.Normal, palette.Selected);
        return new ListItemView { Index = index, Background = background };
    }

    private (RectTransform accent, RectTransform body) BuildCardShell(
        Transform buttonTransform,
        Color accentColor,
        float bodySpacing,
        RectOffset bodyPadding
    )
    {
        var rootLayout = CreateHorizontalGroup(
            "RootLayout",
            buttonTransform,
            0f,
            null,
            TextAnchor.UpperLeft,
            true,
            true,
            false,
            false
        );
        StretchToParent(rootLayout, 0f, 0f, 0f, 0f);

        var accent = CreateRect("Accent", rootLayout);
        ConfigureLayoutElement(
            accent.gameObject,
            preferredWidth: 6f,
            minWidth: 6f,
            flexibleHeight: 1f
        );
        AddImage(accent.gameObject, accentColor);

        var body = CreateVerticalGroup(
            "Body",
            rootLayout,
            bodySpacing,
            bodyPadding,
            TextAnchor.UpperLeft,
            true,
            true,
            true,
            false
        );
        ConfigureLayoutElement(body.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);

        return (accent, body);
    }

    private void AddPill(
        RectTransform parent,
        string name,
        string label,
        Color bg,
        Color textColor,
        float minWidth
    )
    {
        var displayLabel = FormatPillLabel(label);
        var width = Mathf.Max(minWidth, MeasurePillWidth(displayLabel));
        var pill = CreatePill(parent, name, displayLabel, bg, textColor);
        ConfigureLayoutElement(
            pill.gameObject,
            preferredWidth: width,
            minWidth: width,
            preferredHeight: 22f,
            minHeight: 22f
        );
    }

    private void AddDetailLine(
        RectTransform parent,
        string text,
        int fontSize,
        FontStyle style,
        Color color
    )
    {
        var line = CreateText("Detail", parent, fontSize, style, TextAnchor.UpperLeft);
        line.text = text;
        line.color = color;
        line.textWrappingMode = TextWrappingModes.NoWrap;
        line.overflowMode = TextOverflowModes.Ellipsis;
        var height = fontSize <= 12 ? 16f : 20f;
        ConfigureLayoutElement(line.gameObject, preferredHeight: height, minHeight: height);
    }

    private static void ApplyItemState(
        Image background,
        bool selected,
        Color normal,
        Color selectedColor
    )
    {
        background.color = selected ? selectedColor : normal;
    }

    private static void ClearContainer<TView>(RectTransform container, List<TView> views)
    {
        foreach (Transform child in container)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        views.Clear();
    }

    private void CreatePlaceholder(Transform parent, string message)
    {
        var placeholder = CreateRect("Placeholder", parent);
        ConfigureLayoutElement(placeholder.gameObject, preferredHeight: 96f, minHeight: 96f);
        AddImage(placeholder.gameObject, new Color(0.12f, 0.14f, 0.18f, 0.98f));

        var text = CreateText("Text", placeholder, 14, FontStyle.Normal, TextAnchor.MiddleCenter);
        text.text = message;
        text.color = new Color(0.72f, 0.77f, 0.84f, 0.95f);
        StretchToParent(text.rectTransform, 14f, 14f, 0f, 0f);
    }

    private void BuildBattleNameRow(RectTransform parent, HistoryBattleRecord battle)
    {
        var row = CreateHorizontalGroup(
            "BattleNameRow",
            parent,
            6f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(row.gameObject, preferredHeight: 20f, minHeight: 20f);

        BuildRankBadge(row, battle.OpponentRank, battle.OpponentRating, "OpponentRank");

        var nameText = CreateText("OpponentName", row, 15, FontStyle.Bold, TextAnchor.MiddleLeft);
        nameText.text = battle.OpponentName ?? "Unknown Opponent";
        nameText.color = Color.white;
        nameText.textWrappingMode = TextWrappingModes.NoWrap;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        ConfigureLayoutElement(
            nameText.gameObject,
            flexibleWidth: 1f,
            preferredHeight: 20f,
            minHeight: 20f
        );
    }

    private static string? BuildBattleParticipantSummary(HistoryBattleRecord battle)
    {
        var playerHero = HistoryPanelFormatter.FormatOpponentHero(battle.PlayerHero) ?? "?";
        var opponentHero = HistoryPanelFormatter.FormatOpponentHero(battle.OpponentHero) ?? "?";
        var playerLevel = battle.PlayerLevel?.ToString() ?? "?";
        var opponentLevel = battle.OpponentLevel?.ToString() ?? "?";
        if (playerHero == "?" && opponentHero == "?" && playerLevel == "?" && opponentLevel == "?")
            return null;

        return $"YOU {playerHero} Lv{playerLevel}  |  OPP {opponentHero} Lv{opponentLevel}";
    }

    private void BuildRankBadge(
        RectTransform parent,
        string? rawRank,
        int? rating,
        string badgeName
    )
    {
        var rank = FormatRank(rawRank);
        if (string.IsNullOrWhiteSpace(rank))
            return;

        if (string.Equals(rank, "Legendary", StringComparison.OrdinalIgnoreCase))
        {
            AddPill(
                parent,
                badgeName,
                rating.HasValue ? rating.Value.ToString() : "LEG",
                ColorFromRgb(241, 54, 41),
                Color.white,
                68f
            );
            return;
        }

        var palette = GetRankBadgePalette(rank);
        AddPill(parent, badgeName, rank.ToUpperInvariant(), palette.Background, palette.Text, 68f);
    }

    private void BuildRunRankBadge(RectTransform parent, HistoryRunRecord run)
    {
        if (string.Equals(run.GameMode?.Trim(), "Ranked", StringComparison.OrdinalIgnoreCase))
        {
            BuildRankBadge(parent, run.PlayerRank, run.PlayerRating, "PlayerRank");
            return;
        }

        AddPill(
            parent,
            "PlayerRank",
            "Unrank",
            new Color(0.22f, 0.24f, 0.29f, 0.98f),
            new Color(0.90f, 0.94f, 1f, 1f),
            84f
        );
    }

    private void BuildRunStatStrip(Transform parent, HistoryRunRecord run)
    {
        var row = CreateHorizontalGroup(
            "RunStatsRow",
            parent,
            6f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(row.gameObject, preferredHeight: 40f, minHeight: 40f);

        CreateRunStatChip(row, "HP", run.MaxHealth, new Color(0.63f, 0.98f, 0.35f, 1f));
        CreateRunStatChip(row, "PRE", run.Prestige, new Color(1f, 0.65f, 0.13f, 1f));
        CreateRunStatChip(row, "LVL", run.Level, new Color(0.36f, 0.79f, 1f, 1f));
        CreateRunStatChip(row, "INC", run.Income, new Color(1f, 0.86f, 0.10f, 1f));
        CreateRunStatChip(row, "GLD", run.Gold, new Color(1f, 0.86f, 0.10f, 1f));
    }
}
