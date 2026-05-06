#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanelUiToolkitView
{
    private static VisualElement CreateSectionPanel(float? width)
    {
        var panel = new VisualElement();
        panel.style.backgroundColor = new Color(0.11f, 0.13f, 0.18f, 0.98f);
        panel.style.borderTopLeftRadius = 10f;
        panel.style.borderTopRightRadius = 10f;
        panel.style.borderBottomLeftRadius = 10f;
        panel.style.borderBottomRightRadius = 10f;
        panel.style.paddingLeft = 14f;
        panel.style.paddingRight = 14f;
        panel.style.paddingTop = 14f;
        panel.style.paddingBottom = 14f;
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.overflow = Overflow.Hidden;
        if (width.HasValue)
            panel.style.width = width.Value;
        return panel;
    }

    private static VisualElement CreateListFrame(VisualElement content)
    {
        var frame = new VisualElement();
        frame.style.flexGrow = 1f;
        frame.style.flexShrink = 1f;
        frame.style.minHeight = 0f;
        frame.style.marginTop = 10f;
        frame.style.paddingLeft = 6f;
        frame.style.paddingRight = 6f;
        frame.style.paddingTop = 6f;
        frame.style.paddingBottom = 6f;
        frame.style.backgroundColor = new Color(0.09f, 0.11f, 0.15f, 0.96f);
        frame.style.borderTopLeftRadius = 10f;
        frame.style.borderTopRightRadius = 10f;
        frame.style.borderBottomLeftRadius = 10f;
        frame.style.borderBottomRightRadius = 10f;
        frame.style.borderLeftWidth = 1f;
        frame.style.borderRightWidth = 1f;
        frame.style.borderTopWidth = 1f;
        frame.style.borderBottomWidth = 1f;
        frame.style.borderLeftColor = new Color(0.24f, 0.29f, 0.38f, 0.55f);
        frame.style.borderRightColor = new Color(0.24f, 0.29f, 0.38f, 0.55f);
        frame.style.borderTopColor = new Color(0.24f, 0.29f, 0.38f, 0.55f);
        frame.style.borderBottomColor = new Color(0.24f, 0.29f, 0.38f, 0.55f);
        frame.style.overflow = Overflow.Hidden;

        content.style.marginTop = 0f;
        frame.Add(content);
        return frame;
    }

    private static Label CreateSectionTitle(string text)
    {
        var label = CreateLabel(20, FontStyle.Bold, new Color(0.76f, 0.91f, 1f, 1f));
        label.text = text.ToUpperInvariant();
        label.style.height = 32f;
        label.style.minHeight = 32f;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        return label;
    }

    private static Label CreateChip()
    {
        var chip = CreateLabel(12, FontStyle.Bold, new Color(0.95f, 0.96f, 0.98f, 1f));
        chip.style.backgroundColor = new Color(0.14f, 0.18f, 0.23f, 0.96f);
        chip.style.minWidth = 86f;
        chip.style.height = 32f;
        chip.style.paddingLeft = 8f;
        chip.style.paddingRight = 8f;
        chip.style.unityTextAlign = TextAnchor.MiddleCenter;
        chip.style.borderTopLeftRadius = 10f;
        chip.style.borderTopRightRadius = 10f;
        chip.style.borderBottomLeftRadius = 10f;
        chip.style.borderBottomRightRadius = 10f;
        return chip;
    }

    private static Label CreateLabel(int fontSize, FontStyle fontStyle, Color color)
    {
        var label = new Label();
        label.style.fontSize = fontSize;
        label.style.unityFont = GetUiFont();
        label.style.unityFontStyleAndWeight = fontStyle;
        label.style.color = color;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        return label;
    }

    private static Button CreateButton(string text, Action onClick, float width, float height)
    {
        var button = new Button(() => onClick()) { text = text };
        button.style.width = width;
        button.style.minWidth = width;
        button.style.maxWidth = width;
        button.style.height = height;
        button.style.flexGrow = 0f;
        button.style.flexShrink = 0f;
        button.style.unityFont = GetUiFont();
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.justifyContent = Justify.Center;
        button.style.alignItems = Align.Center;
        button.style.paddingLeft = 0f;
        button.style.paddingRight = 0f;
        button.style.paddingTop = 0f;
        button.style.paddingBottom = 0f;
        button.style.backgroundColor = new Color(0.23f, 0.27f, 0.32f, 0.98f);
        button.style.color = Color.white;
        button.style.borderLeftWidth = 1f;
        button.style.borderRightWidth = 1f;
        button.style.borderTopWidth = 1f;
        button.style.borderBottomWidth = 1f;
        button.style.borderLeftColor = new Color(0.34f, 0.40f, 0.48f, 0.55f);
        button.style.borderRightColor = new Color(0.34f, 0.40f, 0.48f, 0.55f);
        button.style.borderTopColor = new Color(0.34f, 0.40f, 0.48f, 0.55f);
        button.style.borderBottomColor = new Color(0.34f, 0.40f, 0.48f, 0.55f);
        button.style.borderTopLeftRadius = 10f;
        button.style.borderTopRightRadius = 10f;
        button.style.borderBottomLeftRadius = 10f;
        button.style.borderBottomRightRadius = 10f;
        var textElement = button.Q<TextElement>();
        if (textElement != null)
        {
            textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            textElement.style.flexGrow = 1f;
            textElement.style.unityFont = GetUiFont();
        }
        return button;
    }

    private static void StyleButton(Button button, Color background, Color textColor)
    {
        button.style.backgroundColor = background;
        button.style.color = textColor;
        var border = new Color(
            Mathf.Clamp01(background.r + 0.08f),
            Mathf.Clamp01(background.g + 0.08f),
            Mathf.Clamp01(background.b + 0.08f),
            0.58f
        );
        button.style.borderLeftColor = border;
        button.style.borderRightColor = border;
        button.style.borderTopColor = border;
        button.style.borderBottomColor = border;
    }

    private static Font GetUiFont()
    {
        _uiFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return _uiFont;
    }

    private static VisualElement CreateSpacer()
    {
        var spacer = new VisualElement();
        spacer.style.flexGrow = 1f;
        return spacer;
    }

    private static VisualElement CreateRowShell()
    {
        var row = new VisualElement();
        row.style.height = Length.Percent(100);
        row.style.marginBottom = 6f;
        row.style.backgroundColor = new Color(0.11f, 0.14f, 0.18f, 0.98f);
        row.style.borderTopLeftRadius = 8f;
        row.style.borderTopRightRadius = 8f;
        row.style.borderBottomLeftRadius = 8f;
        row.style.borderBottomRightRadius = 8f;
        row.style.flexDirection = FlexDirection.Row;
        row.style.overflow = Overflow.Hidden;
        return row;
    }

    private static VisualElement CreateAccentBar()
    {
        var accent = new VisualElement();
        accent.style.width = 6f;
        accent.style.flexShrink = 0f;
        return accent;
    }

    private static VisualElement CreateRowContent()
    {
        var content = new VisualElement();
        content.style.flexGrow = 1f;
        content.style.paddingLeft = 12f;
        content.style.paddingRight = 12f;
        content.style.paddingTop = 9f;
        content.style.paddingBottom = 9f;
        content.style.flexDirection = FlexDirection.Column;
        return content;
    }

    private static VisualElement CreateRowTopRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        return row;
    }

    private static Label CreateRowTitle(VisualElement row)
    {
        var label = CreateLabel(14, FontStyle.Bold, Color.white);
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.flexGrow = 1f;
        row.Add(label);
        return label;
    }

    private static Label CreateRowCornerLabel(VisualElement row, int fontSize)
    {
        var label = CreateLabel(fontSize, FontStyle.Normal, new Color(0.72f, 0.78f, 0.85f, 0.92f));
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.flexShrink = 0f;
        label.style.marginLeft = 8f;
        row.Add(label);
        return label;
    }

    private static Label CreateInlineText(VisualElement row, int fontSize, Color color)
    {
        var label = CreateLabel(fontSize, FontStyle.Normal, color);
        label.style.whiteSpace = WhiteSpace.NoWrap;
        row.Add(label);
        return label;
    }

    private static Label CreateRowPill(VisualElement row)
    {
        var pill = CreateLabel(10, FontStyle.Bold, Color.white);
        pill.style.minWidth = 58f;
        pill.style.height = 20f;
        pill.style.paddingLeft = 8f;
        pill.style.paddingRight = 8f;
        pill.style.marginLeft = 8f;
        pill.style.unityTextAlign = TextAnchor.MiddleCenter;
        pill.style.borderTopLeftRadius = 10f;
        pill.style.borderTopRightRadius = 10f;
        pill.style.borderBottomLeftRadius = 10f;
        pill.style.borderBottomRightRadius = 10f;
        row.Add(pill);
        return pill;
    }

    private static Label CreateRowMeta(VisualElement row)
    {
        var label = CreateLabel(12, FontStyle.Normal, new Color(0.82f, 0.86f, 0.92f, 0.96f));
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.marginTop = 4f;
        row.Add(label);
        return label;
    }

    private static Label CreateRowDetail(VisualElement row)
    {
        var label = CreateLabel(11, FontStyle.Normal, new Color(0.70f, 0.75f, 0.83f, 0.90f));
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.marginTop = 4f;
        row.Add(label);
        return label;
    }

    private static VisualElement CreateInfoChipRow(
        VisualElement parent,
        float spacing,
        float marginTop
    )
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginTop = marginTop;
        row.style.flexWrap = Wrap.NoWrap;
        parent.Add(row);
        return row;
    }

    private static Label CreateInfoChip(VisualElement row, string label, float minWidth)
    {
        var chip = CreateLabel(10, FontStyle.Bold, Color.white);
        chip.text = label;
        chip.style.minWidth = minWidth;
        chip.style.height = 22f;
        chip.style.marginRight = 6f;
        chip.style.paddingLeft = 8f;
        chip.style.paddingRight = 8f;
        chip.style.unityTextAlign = TextAnchor.MiddleCenter;
        chip.style.borderTopLeftRadius = 7f;
        chip.style.borderTopRightRadius = 7f;
        chip.style.borderBottomLeftRadius = 7f;
        chip.style.borderBottomRightRadius = 7f;
        row.Add(chip);
        return chip;
    }

    private static VisualElement CreateRunBadgeRow(VisualElement parent)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginTop = 6f;
        parent.Add(row);
        return row;
    }

    private static Label CreateInlinePill(VisualElement row, float minWidth)
    {
        var pill = CreateLabel(10, FontStyle.Bold, Color.white);
        pill.style.minWidth = minWidth;
        pill.style.height = 20f;
        pill.style.paddingLeft = 8f;
        pill.style.paddingRight = 8f;
        pill.style.marginRight = 6f;
        pill.style.unityTextAlign = TextAnchor.MiddleCenter;
        pill.style.borderTopLeftRadius = 10f;
        pill.style.borderTopRightRadius = 10f;
        pill.style.borderBottomLeftRadius = 10f;
        pill.style.borderBottomRightRadius = 10f;
        row.Add(pill);
        return pill;
    }

    private static void SetFixedPillWidth(Label pill, float width)
    {
        pill.style.minWidth = width;
        pill.style.maxWidth = width;
        pill.style.width = width;
    }

    private static void SetEqualChipWidth(Label chip, bool isLast = false)
    {
        chip.style.flexGrow = 1f;
        chip.style.flexShrink = 1f;
        chip.style.flexBasis = 0f;
        chip.style.minWidth = 0f;
        chip.style.marginRight = isLast ? 0f : 6f;
    }

    private static Label CreateDayBubble(VisualElement parent)
    {
        var bubble = CreateLabel(11, FontStyle.Bold, Color.white);
        bubble.style.width = 40f;
        bubble.style.height = 40f;
        bubble.style.unityTextAlign = TextAnchor.MiddleCenter;
        bubble.style.borderTopLeftRadius = 20f;
        bubble.style.borderTopRightRadius = 20f;
        bubble.style.borderBottomLeftRadius = 20f;
        bubble.style.borderBottomRightRadius = 20f;
        bubble.style.backgroundColor = new Color(0.24f, 0.28f, 0.36f, 0.98f);
        bubble.style.borderLeftWidth = 1f;
        bubble.style.borderRightWidth = 1f;
        bubble.style.borderTopWidth = 1f;
        bubble.style.borderBottomWidth = 1f;
        bubble.style.borderLeftColor = new Color(0.52f, 0.60f, 0.72f, 0.45f);
        bubble.style.borderRightColor = new Color(0.52f, 0.60f, 0.72f, 0.45f);
        bubble.style.borderTopColor = new Color(0.52f, 0.60f, 0.72f, 0.45f);
        bubble.style.borderBottomColor = new Color(0.52f, 0.60f, 0.72f, 0.45f);
        parent.Add(bubble);
        return bubble;
    }

    private static Label CreateBattleDayBubble(VisualElement parent)
    {
        var bubble = CreateDayBubble(parent);
        bubble.style.fontSize = 14f;
        return bubble;
    }

    private static void ConfigurePill(
        Label pill,
        string text,
        Color background,
        Color textColor,
        bool visible
    )
    {
        pill.text = text;
        pill.style.backgroundColor = background;
        pill.style.color = textColor;
        pill.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static void ConfigureInfoChip(Label chip, string label, string value, Color accent)
    {
        chip.text = $"{label} {value}";
        chip.style.backgroundColor = new Color(
            Mathf.Lerp(0.14f, accent.r, 0.10f),
            Mathf.Lerp(0.16f, accent.g, 0.10f),
            Mathf.Lerp(0.20f, accent.b, 0.10f),
            0.98f
        );
        chip.style.color = accent;
        chip.style.borderLeftWidth = 2f;
        chip.style.borderLeftColor = new Color(accent.r, accent.g, accent.b, 0.95f);
    }

    private static void ConfigureStatusPill(Label pill, string rawStatus)
    {
        var status = HistoryPanelFormatter.FormatRunStatus(rawStatus);
        var background =
            string.Equals(rawStatus, "completed", StringComparison.OrdinalIgnoreCase)
                ? new Color(0.16f, 0.30f, 0.24f, 0.74f)
            : string.Equals(rawStatus, "abandoned", StringComparison.OrdinalIgnoreCase)
                ? new Color(0.31f, 0.22f, 0.15f, 0.72f)
            : new Color(0.18f, 0.24f, 0.33f, 0.72f);
        var text =
            string.Equals(rawStatus, "completed", StringComparison.OrdinalIgnoreCase)
                ? new Color(0.82f, 0.98f, 0.90f, 0.90f)
            : string.Equals(rawStatus, "abandoned", StringComparison.OrdinalIgnoreCase)
                ? new Color(0.99f, 0.90f, 0.85f, 0.88f)
            : new Color(0.84f, 0.92f, 1f, 0.88f);
        ConfigurePill(pill, status, background, text, true);
    }

    private static void BindHeroPill(Label pill, string? rawHero)
    {
        var hero = HistoryPanelFormatter.FormatOpponentHero(rawHero);
        if (string.IsNullOrWhiteSpace(hero))
        {
            ConfigurePill(pill, string.Empty, Color.clear, Color.clear, false);
            return;
        }

        var heroStyle = GetHeroBadgeStyle(hero);
        ConfigurePill(pill, heroStyle.ShortCode, heroStyle.Background, heroStyle.Text, true);
    }

    private static void BindBattleRankPill(Label pill, string? rawRank, int? rating)
    {
        var rank = HistoryPanelFormatter.NormalizeRank(rawRank);
        if (string.Equals(rank, "Legendary", StringComparison.OrdinalIgnoreCase))
        {
            ConfigurePill(
                pill,
                HistoryPanelText.RankLabel(rank, rating),
                ColorFromRgb(241, 54, 41),
                Color.white,
                true
            );
            return;
        }

        if (string.IsNullOrWhiteSpace(rank))
        {
            ConfigurePill(pill, string.Empty, Color.clear, Color.clear, false);
            return;
        }

        var palette = GetRankBadgePalette(rank);
        ConfigurePill(
            pill,
            HistoryPanelText.RankLabel(rank),
            palette.Background,
            palette.Text,
            true
        );
    }

    private static void BindRunOutcomeBubble(Label bubble, HistoryRunRecord run)
    {
        var tier = HistoryPanelFormatter.GetRunOutcomeTier(run) ?? RunOutcomeTier.Misfortune;
        Color background;
        Color border;

        if (tier == RunOutcomeTier.Diamond)
        {
            background = new Color(0.15f, 0.34f, 0.46f, 0.98f);
            border = new Color(0.42f, 0.78f, 0.98f, 0.42f);
        }
        else if (tier == RunOutcomeTier.Gold)
        {
            background = new Color(0.37f, 0.28f, 0.10f, 0.98f);
            border = new Color(0.86f, 0.68f, 0.24f, 0.42f);
        }
        else if (tier == RunOutcomeTier.Silver)
        {
            background = new Color(0.31f, 0.34f, 0.40f, 0.98f);
            border = new Color(0.74f, 0.80f, 0.90f, 0.42f);
        }
        else if (tier == RunOutcomeTier.Bronze)
        {
            background = new Color(0.36f, 0.22f, 0.15f, 0.98f);
            border = new Color(0.78f, 0.52f, 0.36f, 0.42f);
        }
        else
        {
            background = new Color(0.25f, 0.18f, 0.18f, 0.98f);
            border = new Color(0.64f, 0.38f, 0.38f, 0.42f);
        }

        bubble.text = HistoryPanelText.RunOutcomeBubbleLabel(tier);
        bubble.style.backgroundColor = background;
        bubble.style.borderLeftColor = border;
        bubble.style.borderRightColor = border;
        bubble.style.borderTopColor = border;
        bubble.style.borderBottomColor = border;
    }

    private static void RefreshTabButton(Button button, bool selected)
    {
        if (selected)
        {
            StyleButton(
                button,
                new Color(0.78f, 0.60f, 0.24f, 0.98f),
                new Color(0.10f, 0.07f, 0.03f, 1f)
            );
            return;
        }

        StyleButton(button, new Color(0.25f, 0.30f, 0.37f, 0.98f), Color.white);
    }

    private static void RefreshGhostFilterButton(Button button, bool selected)
    {
        if (selected)
        {
            StyleButton(
                button,
                new Color(0.78f, 0.60f, 0.24f, 0.98f),
                new Color(0.10f, 0.07f, 0.03f, 1f)
            );
            return;
        }

        StyleButton(button, new Color(0.19f, 0.22f, 0.27f, 0.98f), Color.white);
    }

    private static void RefreshDeleteButton(Button button, string text, bool enabled)
    {
        var isConfirmState = string.Equals(
            text,
            HistoryPanelText.DeleteConfirm(),
            StringComparison.Ordinal
        );
        if (isConfirmState)
        {
            StyleButton(
                button,
                new Color(0.60f, 0.19f, 0.16f, 0.98f),
                new Color(1f, 0.94f, 0.92f, 1f)
            );
            return;
        }

        if (!enabled)
        {
            StyleButton(
                button,
                new Color(0.28f, 0.20f, 0.19f, 0.88f),
                new Color(0.86f, 0.82f, 0.80f, 0.88f)
            );
            return;
        }

        StyleButton(button, new Color(0.40f, 0.24f, 0.20f, 0.98f), new Color(1f, 0.93f, 0.90f, 1f));
    }

    private static string ShortenBattleId(string battleId)
    {
        return string.IsNullOrWhiteSpace(battleId) ? "-"
            : battleId.Length <= 12 ? battleId
            : battleId[..12];
    }

    private readonly struct HeroBadgeStyle
    {
        public HeroBadgeStyle(string shortCode, Color background, Color text)
        {
            ShortCode = shortCode;
            Background = background;
            Text = text;
        }

        public string ShortCode { get; }

        public Color Background { get; }

        public Color Text { get; }
    }

    private static Color GetRunAchievementBackground(string achievement)
    {
        return achievement switch
        {
            "PERFECT" => new Color(0.36f, 0.28f, 0.10f, 0.98f),
            "GOLD" => new Color(0.41f, 0.31f, 0.12f, 0.98f),
            "SILVER" => new Color(0.36f, 0.38f, 0.44f, 0.98f),
            "BRONZE" => new Color(0.38f, 0.25f, 0.18f, 0.98f),
            _ => new Color(0.24f, 0.18f, 0.18f, 0.98f),
        };
    }

    private static Color GetRunAchievementText(string achievement)
    {
        return achievement switch
        {
            "PERFECT" => new Color(1f, 0.94f, 0.71f, 1f),
            "GOLD" => new Color(0.99f, 0.90f, 0.66f, 1f),
            "SILVER" => new Color(0.94f, 0.97f, 1f, 1f),
            "BRONZE" => new Color(0.98f, 0.88f, 0.80f, 1f),
            _ => new Color(0.96f, 0.84f, 0.84f, 1f),
        };
    }

    private static string? FormatRank(string? rawRank)
    {
        if (string.IsNullOrWhiteSpace(rawRank))
            return null;

        var trimmed = rawRank.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace > 0 ? trimmed[..firstSpace] : trimmed;
    }

    private static (Color Background, Color Text) GetRankBadgePalette(string rank)
    {
        return rank switch
        {
            "Bronze" => (new Color(0.39f, 0.24f, 0.17f, 0.98f), new Color(0.98f, 0.88f, 0.80f, 1f)),
            "Silver" => (new Color(0.34f, 0.37f, 0.43f, 0.98f), new Color(0.94f, 0.97f, 1f, 1f)),
            "Gold" => (new Color(0.41f, 0.31f, 0.12f, 0.98f), new Color(0.99f, 0.90f, 0.66f, 1f)),
            "Diamond" => (new Color(0.18f, 0.35f, 0.47f, 0.98f), new Color(0.84f, 0.97f, 1f, 1f)),
            _ => (new Color(0.24f, 0.28f, 0.36f, 0.98f), new Color(0.89f, 0.94f, 1f, 1f)),
        };
    }

    private static HeroBadgeStyle GetHeroBadgeStyle(string? heroName)
    {
        if (string.IsNullOrWhiteSpace(heroName))
            return new HeroBadgeStyle("UNK", new Color(0.20f, 0.29f, 0.38f, 0.95f), Color.white);

        return heroName.Trim() switch
        {
            "Vanessa" => BuildHeroBadgeStyle("VAN", 192, 33, 33),
            "Pygmalien" => BuildHeroBadgeStyle("PYG", 39, 103, 192),
            "Dooley" => BuildHeroBadgeStyle("DOO", 225, 154, 8),
            "Mak" => BuildHeroBadgeStyle("MAK", 190, 230, 91),
            "Jules" => BuildHeroBadgeStyle("JUL", 180, 52, 236),
            "Karnok" => BuildHeroBadgeStyle("KAR", 59, 136, 156),
            "Stelle" => BuildHeroBadgeStyle("STE", 255, 235, 24),
            _ => BuildHeroBadgeStyle(
                heroName.Length <= 3
                    ? heroName.ToUpperInvariant()
                    : heroName[..3].ToUpperInvariant(),
                57,
                73,
                97
            ),
        };
    }

    private static HeroBadgeStyle BuildHeroBadgeStyle(string shortCode, int r, int g, int b)
    {
        var background = ColorFromRgb(r, g, b);
        var luminance = (0.299f * background.r) + (0.587f * background.g) + (0.114f * background.b);
        var text = luminance > 0.62f ? new Color(0.10f, 0.12f, 0.15f, 1f) : Color.white;
        return new HeroBadgeStyle(shortCode, background, text);
    }

    private static Color ColorFromRgb(int r, int g, int b)
    {
        return new Color(r / 255f, g / 255f, b / 255f, 0.98f);
    }
}
