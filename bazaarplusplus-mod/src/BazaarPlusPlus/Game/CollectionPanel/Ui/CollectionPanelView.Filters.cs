#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop.EncounterPortraits;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.TagTypography;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

internal sealed partial class CollectionPanelView
{
    private void EnsureHeroChips(IReadOnlyList<EHero> heroes)
    {
        if (_heroChipRow == null)
            return;
        if (HeroChipsMatch(heroes))
            return;
        ClearHeroChipRow();
        for (var i = 0; i < heroes.Count; i++)
        {
            var hero = heroes[i];
            var chip = CreateHeroChipButton(hero, () => _commands.ToggleHero(hero));
            chip.style.marginRight =
                i % Sizes.HeroChipsPerRow == Sizes.HeroChipsPerRow - 1 ? 0f : UiSpacing.Sm;
            _heroChips[hero] = chip;
            _heroChipRow.Add(chip);
        }
    }

    private void EnsureTierChips(IReadOnlyList<ETier> tiers)
    {
        if (_tierChipRow == null)
            return;
        if (TierChipsMatch(tiers))
            return;
        ClearChipRow(_tierChips, _tierChipRow, keepFirst: false);
        var index = 0;
        foreach (var tier in tiers)
        {
            var chip = CreateChipButton(
                CollectionPanelText.Tier(tier),
                () => _commands.ToggleTier(tier),
                true
            );
            chip.style.marginLeft = index > 0 ? UiSpacing.Sm : 0f;
            _tierChips[tier] = chip;
            _tierChipRow.Add(chip);
            index++;
        }
    }

    private bool HeroChipsMatch(IReadOnlyList<EHero> heroes)
    {
        if (heroes.Count != _heroChips.Count)
            return false;
        foreach (var hero in heroes)
        {
            if (!_heroChips.ContainsKey(hero))
                return false;
        }
        return true;
    }

    private bool TierChipsMatch(IReadOnlyList<ETier> tiers)
    {
        if (tiers.Count != _tierChips.Count)
            return false;
        foreach (var tier in tiers)
        {
            if (!_tierChips.ContainsKey(tier))
                return false;
        }
        return true;
    }

    private void EnsureSizeChips(IReadOnlyList<ECardSize> sizes)
    {
        if (_sizeChipRow == null)
            return;
        if (SizeChipsMatch(sizes))
            return;
        ClearChipRow(_sizeChips, _sizeChipRow, keepFirst: false);
        var index = 0;
        foreach (var size in sizes)
        {
            var chip = CreateChipButton(
                CollectionPanelText.Size(size),
                () => _commands.ToggleSize(size),
                true
            );
            chip.style.marginLeft = index > 0 ? UiSpacing.Sm : 0f;
            _sizeChips[size] = chip;
            _sizeChipRow.Add(chip);
            index++;
        }
    }

    private bool SizeChipsMatch(IReadOnlyList<ECardSize> sizes)
    {
        if (sizes.Count != _sizeChips.Count)
            return false;
        foreach (var size in sizes)
        {
            if (!_sizeChips.ContainsKey(size))
                return false;
        }
        return true;
    }

    private void RefreshFacetChips(CollectionPanelViewModel model)
    {
        EnsureTagChips(model.AvailableTags);

        if (model.TabProfile.ShowKeywordFilter)
        {
            EnsureKeywordChips(model.AvailableKeywords);
        }
        else
        {
            ClearKeywordFacetRow();
        }
    }

    private void EnsureTagChips(IReadOnlyList<ECardTag> tags)
    {
        if (_tagChipRow == null)
            return;
        if (!TagChipsMatch(tags))
        {
            ClearTagFacetRow();
            foreach (var tag in tags)
            {
                var captured = tag;
                var chip = CreateTagFacetChipButton(() => _commands.ToggleTag(captured));
                ApplyTagChipContent(chip, NativeTagTypography.Resolve(captured));
                _tagChips[captured] = chip;
                _tagChipOrder.Add(captured);
                _tagChipRow.Add(chip);
            }
        }
    }

    private void EnsureKeywordChips(IReadOnlyList<EHiddenTag> keywords)
    {
        if (_keywordChipRow == null)
            return;
        if (!KeywordChipsMatch(keywords))
        {
            ClearKeywordFacetRow();
            var hasReferenceSection = false;
            foreach (var keyword in keywords)
            {
                if (!hasReferenceSection && CollectionKeywordWhitelist.IsReferenceKeyword(keyword))
                {
                    _keywordReferenceSectionLabel = CreateKeywordReferenceSectionLabel();
                    _keywordChipRow.Add(_keywordReferenceSectionLabel);
                    hasReferenceSection = true;
                }

                var captured = keyword;
                var chip = CreateTagFacetChipButton(() => _commands.ToggleKeyword(captured));
                ApplyTagChipContent(chip, NativeTagTypography.Resolve(captured));
                _keywordChips[captured] = chip;
                _keywordChipOrder.Add(captured);
                _keywordChipRow.Add(chip);
            }
        }
    }

    private bool TagChipsMatch(IReadOnlyList<ECardTag> visible)
    {
        if (visible.Count != _tagChipOrder.Count)
            return false;
        for (var i = 0; i < visible.Count; i++)
            if (visible[i] != _tagChipOrder[i])
                return false;
        return true;
    }

    private bool KeywordChipsMatch(IReadOnlyList<EHiddenTag> visible)
    {
        if (visible.Count != _keywordChipOrder.Count)
            return false;
        for (var i = 0; i < visible.Count; i++)
            if (visible[i] != _keywordChipOrder[i])
                return false;
        return true;
    }

    private void ClearTagFacetRow()
    {
        foreach (var button in _tagChips.Values)
        {
            if (button.parent != null)
                button.parent.Remove(button);
        }
        _tagChips.Clear();
        _tagChipOrder.Clear();
        _tagChipRow?.Clear();
    }

    private void ClearKeywordFacetRow()
    {
        foreach (var button in _keywordChips.Values)
        {
            if (button.parent != null)
                button.parent.Remove(button);
        }
        _keywordChips.Clear();
        _keywordChipOrder.Clear();
        _keywordReferenceSectionLabel = null;
        _keywordChipRow?.Clear();
    }

    private static Label CreateKeywordReferenceSectionLabel()
    {
        var label = CreateLabel(Sizes.FontTiny, FontStyle.Bold, Colors.HistoryStatusText);
        label.text = CollectionPanelText.KeywordReferenceSection();
        label.style.width = Length.Percent(100f);
        label.style.flexBasis = Length.Percent(100f);
        label.style.marginTop = UiSpacing.Xs;
        label.style.marginBottom = UiSpacing.Xs;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        label.style.opacity = 0.72f;
        return label;
    }

    private void EnsureSourceChips(IReadOnlyList<CollectionSourceOptionViewModel> sources)
    {
        if (_sourceChipRow == null)
            return;
        if (SourceChipsMatch(sources))
            return;
        ClearSourceChipRow();
        VisualElement? sourceLine = null;
        for (var i = 0; i < sources.Count; i++)
        {
            if (i % Sizes.SourceChipsPerRow == 0)
            {
                sourceLine = CreateSourceChipLine();
                _sourceChipRow.Add(sourceLine);
            }

            var source = sources[i];
            var chip = CreateSourceChipButton(
                source,
                () => _commands.ToggleSource(source.SourceKey)
            );
            chip.style.marginRight =
                i % Sizes.SourceChipsPerRow == Sizes.SourceChipsPerRow - 1 ? 0f : UiSpacing.Sm;
            _sourceChips[source.SourceKey] = chip;
            _sourceChipOrder.Add(source.SourceKey);
            sourceLine!.Add(chip);
        }
    }

    private bool SourceChipsMatch(IReadOnlyList<CollectionSourceOptionViewModel> sources)
    {
        if (sources.Count != _sourceChips.Count)
            return false;
        if (_sourceChipOrder.Count != sources.Count)
            return false;
        for (var i = 0; i < sources.Count; i++)
            if (!string.Equals(_sourceChipOrder[i], sources[i].SourceKey, StringComparison.Ordinal))
                return false;
        return true;
    }

    private void ClearHeroChipRow()
    {
        foreach (var button in _heroChips.Values)
        {
            if (button.parent != null)
                button.parent.Remove(button);
        }

        _heroChips.Clear();
        _heroChipIcons.Clear();
    }

    private void ClearSourceChipRow()
    {
        foreach (var button in _sourceChips.Values)
        {
            if (button.parent != null)
                button.parent.Remove(button);
        }

        _sourceChips.Clear();
        _sourceChipIcons.Clear();
        _sourceChipOrder.Clear();
        _sourceChipRow?.Clear();
    }

    private static VisualElement CreateSourceChipLine()
    {
        var row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.NoWrap;
        row.style.alignItems = Align.Center;
        row.style.alignSelf = Align.Stretch;
        return row;
    }

    private void OnHeroChipRowGeometryChanged(GeometryChangedEvent evt) =>
        ApplyHeroChipSizing(evt.newRect.width);

    private void ApplyHeroChipSizing(float rowWidth)
    {
        var box = CalculatePortraitChipBox(rowWidth, Sizes.HeroChipsPerRow);
        if (box <= 0f)
            return;
        if (Mathf.Abs(box - _appliedHeroChipBox) < 0.5f)
            return;

        _appliedHeroChipBox = box;
        var icon = Mathf.Round(box * Sizes.SourceChipIconRatio);
        foreach (var pair in _heroChips)
        {
            if (_heroChipIcons.TryGetValue(pair.Key, out var iconElement))
                ResizeHeroChip(pair.Value, iconElement, pair.Key, box, icon);
        }
    }

    private void OnSourceChipRowGeometryChanged(GeometryChangedEvent evt) =>
        ApplySourceChipSizing(evt.newRect.width);

    private void ApplySourceChipSizing(float rowWidth)
    {
        var box = CalculatePortraitChipBox(rowWidth, Sizes.SourceChipsPerRow);
        if (box <= 0f)
            return;
        if (Mathf.Abs(box - _appliedSourceChipBox) < 0.5f)
            return;

        _appliedSourceChipBox = box;
        var icon = Mathf.Round(box * Sizes.SourceChipIconRatio);
        foreach (var pair in _sourceChips)
        {
            if (_sourceChipIcons.TryGetValue(pair.Key, out var iconElement))
                ResizeSourceChip(pair.Value, iconElement, box, icon);
        }
    }

    private static float CalculatePortraitChipBox(float rowWidth, int chipsPerRow)
    {
        if (float.IsNaN(rowWidth) || rowWidth <= 0f || chipsPerRow <= 0)
            return 0f;
        return Mathf.Floor((rowWidth - UiSpacing.Sm * (chipsPerRow - 1)) / chipsPerRow);
    }

    private static void ClearChipRow<T>(
        Dictionary<T, Button> chips,
        VisualElement row,
        bool keepFirst
    )
    {
        foreach (var button in chips.Values)
        {
            if (button.parent != null)
                button.parent.Remove(button);
        }
        chips.Clear();
        if (!keepFirst && row.childCount > 0)
            row.Clear();
    }

    private Button CreateChipButton(string text, Action onClick, bool fillRow = false)
    {
        var chip = CreateButton(
            text,
            onClick,
            fillRow ? 0f : Sizes.ChipMinWidth + 12f,
            Sizes.ChipHeight,
            fixedWidth: !fillRow
        );
        if (fillRow)
        {
            chip.style.flexBasis = 0f;
            chip.style.flexGrow = 1f;
            chip.style.flexShrink = 1f;
            chip.style.minWidth = 0f;
        }
        chip.style.marginRight = fillRow ? 0f : UiSpacing.Sm;
        chip.style.marginBottom = UiSpacing.Xs;
        StyleButton(chip, Colors.HistoryChipBackground, Colors.HistoryChipText);
        return chip;
    }

    private static Button CreateTagFacetChipButton(Action onClick)
    {
        var chip = CreateButton(string.Empty, onClick, 0f, Sizes.InfoChipHeight, fixedWidth: false);
        chip.style.minWidth = Sizes.InfoChipMinWidth;
        chip.style.flexDirection = FlexDirection.Row;
        chip.style.alignItems = Align.Center;
        chip.style.justifyContent = Justify.Center;
        UiStyle.HorizontalPadding(chip.style, UiSpacing.Md);
        chip.style.marginRight = UiSpacing.Sm;
        chip.style.marginBottom = UiSpacing.Xs;

        var icon = new VisualElement { name = TagChipIconName, pickingMode = PickingMode.Ignore };
        UiStyle.FixedSize(icon.style, Sizes.TagChipIconSize, Sizes.TagChipIconSize);
        icon.style.flexShrink = 0f;
        icon.style.marginRight = UiSpacing.Xs;
        icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        icon.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
        icon.style.display = DisplayStyle.None;
        chip.Add(icon);

        var label = new Label { name = TagChipLabelName, pickingMode = PickingMode.Ignore };
        label.style.fontSize = Sizes.FontSmall;
        label.style.unityFont = BppUiFont.Default;
        label.style.unityFontStyleAndWeight = FontStyle.Normal;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.flexShrink = 1f;
        label.style.minWidth = 0f;
        label.style.maxWidth = Sizes.TagFacetChipMaxWidth;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        chip.Add(label);

        StyleButton(chip, Colors.HistoryChipBackground, Colors.HistoryChipText);
        return chip;
    }

    private static void ApplyTagChipContent(Button chip, NativeTagDisplay display)
    {
        var label = chip.Q<Label>(TagChipLabelName);
        if (label != null)
            label.text = StablePanelText.Compact(display.Label, 24);
        chip.tooltip = display.Label;

        var icon = chip.Q<VisualElement>(TagChipIconName);
        if (icon == null)
            return;

        var sprite = KeywordIconSpriteProvider.Resolve(display.IconName);
        if (sprite != null)
        {
            icon.style.backgroundImage = new StyleBackground(sprite);
            icon.style.display = DisplayStyle.Flex;
            icon.MarkDirtyRepaint();
            return;
        }

        icon.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
        icon.style.display = DisplayStyle.None;
    }

    private Button CreateHeroChipButton(EHero hero, Action onClick)
    {
        var labelText = CollectionPanelText.Hero(hero);
        var chip = CreateButton(
            string.Empty,
            onClick,
            CurrentHeroChipBox(),
            CurrentHeroChipBox(),
            fixedWidth: false
        );
        chip.tooltip = labelText;
        chip.style.flexDirection = FlexDirection.Row;
        chip.style.justifyContent = Justify.Center;
        chip.style.alignItems = Align.Center;
        chip.style.marginBottom = UiSpacing.Xs;
        StyleButton(chip, Colors.HistoryChipBackground, Colors.HistoryChipText);

        var icon = CreateHeroChipIcon(hero);
        chip.Add(icon);
        ResizeHeroChip(
            chip,
            icon,
            hero,
            CurrentHeroChipBox(),
            Mathf.Round(CurrentHeroChipBox() * Sizes.SourceChipIconRatio)
        );

        _heroChipIcons[hero] = icon;
        if (HeroPortraitSpriteProvider.IsRenderableHero(hero))
            LoadHeroChipIcon(hero, icon);
        return chip;
    }

    private Button CreateSourceChipButton(CollectionSourceOptionViewModel source, Action onClick)
    {
        var chip = CreateButton(
            string.Empty,
            onClick,
            CurrentSourceChipBox(),
            CurrentSourceChipBox(),
            fixedWidth: false
        );
        chip.tooltip = string.IsNullOrWhiteSpace(source.Description)
            ? source.DisplayName
            : $"{source.DisplayName} - {source.Description}";
        chip.style.flexDirection = FlexDirection.Row;
        chip.style.justifyContent = Justify.Center;
        chip.style.alignItems = Align.Center;
        chip.style.marginRight = UiSpacing.Sm;
        chip.style.marginBottom = UiSpacing.Xs;
        StyleButton(chip, Colors.HistoryChipBackground, Colors.HistoryChipText);

        var icon = CreateSourceChipIcon(source.DisplayName);
        chip.Add(icon);
        var box = CurrentSourceChipBox();
        ResizeSourceChip(chip, icon, box, Mathf.Round(box * Sizes.SourceChipIconRatio));

        _sourceChipIcons[source.SourceKey] = icon;
        LoadSourceChipIcon(source.SourceKey, source.RepresentativeTemplateId, icon);
        return chip;
    }

    private static VisualElement CreateHeroChipIcon(EHero hero)
    {
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.style.position = Position.Relative;
        icon.style.backgroundColor = Colors.HistoryStatusBackground;
        icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
        icon.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
        UiStyle.Border(icon.style, Borders.Thin, Colors.HistoryButtonBorder);
        ResizeHeroIcon(icon, Sizes.HeroChipIconSize);

        if (!HeroPortraitSpriteProvider.IsRenderableHero(hero))
            AddCommonHeroGlyph(icon, Sizes.HeroChipIconSize);

        return icon;
    }

    private static VisualElement CreateSourceChipIcon(string displayName)
    {
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.style.position = Position.Relative;
        icon.style.backgroundColor = Colors.HistoryStatusBackground;
        icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
        icon.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
        UiStyle.Border(icon.style, Borders.Thin, Colors.HistoryButtonBorder);
        ResizeSourceIcon(icon, Mathf.Round(Sizes.SourceChipMinSize * Sizes.SourceChipIconRatio));

        var initials = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistoryChipText);
        initials.name = SourceChipInitialsName;
        initials.text = GetInitials(displayName);
        initials.pickingMode = PickingMode.Ignore;
        initials.style.position = Position.Absolute;
        initials.style.left = 0f;
        initials.style.right = 0f;
        initials.style.top = 0f;
        initials.style.bottom = 0f;
        initials.style.unityTextAlign = TextAnchor.MiddleCenter;
        icon.Add(initials);
        return icon;
    }

    private float CurrentSourceChipBox() =>
        _appliedSourceChipBox > 0f ? _appliedSourceChipBox : Sizes.SourceChipMinSize;

    private float CurrentHeroChipBox() =>
        _appliedHeroChipBox > 0f ? _appliedHeroChipBox : Sizes.HeroChipButtonSize;

    private static void ResizeHeroChip(
        Button chip,
        VisualElement icon,
        EHero hero,
        float box,
        float iconSize
    )
    {
        chip.style.width = box;
        chip.style.minWidth = box;
        chip.style.maxWidth = box;
        chip.style.height = box;
        chip.style.minHeight = box;
        chip.style.maxHeight = box;
        ResizeHeroIcon(icon, iconSize);
        if (!HeroPortraitSpriteProvider.IsRenderableHero(hero))
            AddCommonHeroGlyph(icon, iconSize);
    }

    private static void ResizeSourceChip(Button chip, VisualElement icon, float box, float iconSize)
    {
        chip.style.width = box;
        chip.style.minWidth = box;
        chip.style.maxWidth = box;
        chip.style.height = box;
        chip.style.minHeight = box;
        chip.style.maxHeight = box;
        ResizeSourceIcon(icon, iconSize);
    }

    private static void ResizeHeroIcon(VisualElement icon, float iconSize)
    {
        icon.style.width = iconSize;
        icon.style.minWidth = iconSize;
        icon.style.maxWidth = iconSize;
        icon.style.height = iconSize;
        icon.style.minHeight = iconSize;
        icon.style.maxHeight = iconSize;
        UiStyle.Radius(icon.style, iconSize / 2f);
    }

    private static void ResizeSourceIcon(VisualElement icon, float iconSize)
    {
        icon.style.width = iconSize;
        icon.style.minWidth = iconSize;
        icon.style.maxWidth = iconSize;
        icon.style.height = iconSize;
        icon.style.minHeight = iconSize;
        icon.style.maxHeight = iconSize;
        UiStyle.Radius(icon.style, iconSize / 2f);
    }

    private static void AddCommonHeroGlyph(VisualElement icon, float iconSize)
    {
        icon.Clear();
        AddCommonHeroDot(icon, 0.5f, 0.5f, 0.125f, iconSize, Colors.HistoryChipText);
        AddCommonHeroDot(icon, 0.24f, 0.24f, 0.104f, iconSize, Colors.HistorySubtitleText);
        AddCommonHeroDot(icon, 0.76f, 0.24f, 0.104f, iconSize, Colors.HistorySubtitleText);
        AddCommonHeroDot(icon, 0.24f, 0.76f, 0.104f, iconSize, Colors.HistorySubtitleText);
        AddCommonHeroDot(icon, 0.76f, 0.76f, 0.104f, iconSize, Colors.HistorySubtitleText);
    }

    private static void AddCommonHeroDot(
        VisualElement parent,
        float centerX,
        float centerY,
        float sizeRatio,
        float iconSize,
        Color color
    )
    {
        var size = Mathf.Max(3f, Mathf.Round(iconSize * sizeRatio));
        var dot = new VisualElement { pickingMode = PickingMode.Ignore };
        dot.style.position = Position.Absolute;
        dot.style.left = Mathf.Round(iconSize * centerX - size / 2f);
        dot.style.top = Mathf.Round(iconSize * centerY - size / 2f);
        UiStyle.FixedSize(dot.style, size, size);
        dot.style.backgroundColor = color;
        UiStyle.Radius(dot.style, size / 2f);
        parent.Add(dot);
    }

    private static void LoadHeroChipIcon(EHero hero, VisualElement icon)
    {
        icon.userData = hero;

        if (HeroPortraitSpriteProvider.TryGetCached(hero, out var cached))
        {
            ApplyHeroChipIcon(icon, cached);
            return;
        }

        ApplyHeroChipIcon(icon, null);
        _ = ApplyHeroChipIconWhenLoadedAsync(hero, icon);
    }

    private static void LoadSourceChipIcon(
        string sourceKey,
        Guid representativeTemplateId,
        VisualElement icon
    )
    {
        icon.userData = sourceKey;

        if (EncounterPortraitSpriteProvider.TryGetCached(representativeTemplateId, out var cached))
        {
            ApplySourceChipIcon(icon, cached);
            return;
        }

        ApplySourceChipIcon(icon, null);
        _ = ApplySourceChipIconWhenLoadedAsync(sourceKey, representativeTemplateId, icon);
    }

    private static async System.Threading.Tasks.Task ApplyHeroChipIconWhenLoadedAsync(
        EHero hero,
        VisualElement icon
    )
    {
        var sprite = await HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(hero);
        if (!Equals(icon.userData, hero))
            return;
        ApplyHeroChipIcon(icon, sprite);
    }

    private static async System.Threading.Tasks.Task ApplySourceChipIconWhenLoadedAsync(
        string sourceKey,
        Guid representativeTemplateId,
        VisualElement icon
    )
    {
        var sprite = await EncounterPortraitSpriteProvider.LoadPortraitAsync(
            representativeTemplateId
        );
        if (!Equals(icon.userData, sourceKey))
            return;
        ApplySourceChipIcon(icon, sprite);
    }

    private static void ApplyHeroChipIcon(VisualElement icon, Sprite? sprite)
    {
        if (sprite == null)
        {
            icon.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
            return;
        }

        icon.style.backgroundImage = new StyleBackground(sprite);
        icon.MarkDirtyRepaint();
    }

    private static void ApplySourceChipIcon(VisualElement icon, Sprite? sprite)
    {
        var initials = icon.Q<Label>(SourceChipInitialsName);
        if (sprite == null)
        {
            icon.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
            if (initials != null)
                initials.style.display = DisplayStyle.Flex;
            return;
        }

        icon.style.backgroundImage = new StyleBackground(sprite);
        if (initials != null)
            initials.style.display = DisplayStyle.None;
        icon.MarkDirtyRepaint();
    }

    private static string GetInitials(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "?";

        var initials = new List<char>(2);
        foreach (
            var part in displayName.Split(
                new[] { ' ', '-', '_' },
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            initials.Add(char.ToUpperInvariant(part[0]));
            if (initials.Count == 2)
                break;
        }

        if (initials.Count == 0)
            return "?";
        return new string(initials.ToArray());
    }

    // unselectedTextColor carries the game's official keyword color for tag chips; the selected
    // state keeps the gold highlight regardless so selection always reads the same way.
    private static void RefreshChip(Button chip, bool selected, Color? unselectedTextColor = null)
    {
        if (selected)
        {
            chip.style.backgroundColor = Colors.ButtonSelectedBackground;
            chip.style.color = Colors.ButtonSelectedText;
            UiStyle.BorderColor(
                chip.style,
                Colors.ButtonBorderFor(Colors.ButtonSelectedBackground)
            );
        }
        else
        {
            chip.style.backgroundColor = Colors.HistoryChipBackground;
            chip.style.color = unselectedTextColor ?? Colors.HistoryChipText;
            UiStyle.BorderColor(chip.style, Colors.ButtonBorderFor(Colors.HistoryChipBackground));
        }
    }

    private static void RefreshTierChip(ETier tier, Button chip, bool selected)
    {
        var textColor = TierTextColor(tier);
        RefreshChip(chip, selected, textColor);
        chip.style.color = textColor;
    }

    private static Color TierTextColor(ETier tier) =>
        tier switch
        {
            ETier.Bronze => Colors.FromRgb(180, 98, 65, 1f),
            ETier.Silver => Colors.FromRgb(192, 192, 192, 1f),
            ETier.Gold => Colors.FromRgb(255, 215, 0, 1f),
            ETier.Diamond => Colors.FromRgb(0, 255, 255, 1f),
            ETier.Legendary => Colors.FromRgb(255, 69, 0, 1f),
            _ => Colors.HistoryChipText,
        };

    private void RefreshPackageToggle(bool selected)
    {
        if (_packageToggleButton == null)
            return;

        RefreshTabButton(_packageToggleButton, selected);
    }

    private static void RefreshMatchModeButton(
        Button? button,
        CollectionFacetMatchMode mode,
        string tooltip
    )
    {
        if (button == null)
            return;

        button.text = CollectionPanelText.FacetMatchMode(mode);
        button.tooltip = tooltip;
        RefreshChip(button, mode == CollectionFacetMatchMode.All);
    }

    // Always visible; the face shows the effective day number and highlights when the day
    // participates in filtering (gold = on, chip background = off).
    private void RefreshDayToggle(int day, bool active)
    {
        if (_dayToggleButton == null)
            return;

        _dayToggleButton.text = day.ToString(System.Globalization.CultureInfo.InvariantCulture);
        StyleButton(
            _dayToggleButton,
            active ? Colors.ButtonSelectedBackground : Colors.HistoryChipBackground,
            active ? Colors.ButtonSelectedText : Colors.HistoryChipText
        );
    }

    private void RefreshHeroChip(EHero hero, Button chip, bool selected)
    {
        RefreshChip(chip, selected);
    }

    private static void RefreshTabButton(Button button, bool selected)
    {
        if (selected)
            StyleButton(button, Colors.ButtonSelectedBackground, Colors.ButtonSelectedText);
        else
            StyleButton(button, Colors.RunsTabBackground, Colors.White);
    }

    private static Label CreateLabel(int fontSize, FontStyle fontStyle, Color color)
    {
        var label = new Label();
        label.style.fontSize = fontSize;
        label.style.unityFont = BppUiFont.Default;
        label.style.unityFontStyleAndWeight = fontStyle;
        label.style.color = color;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        return label;
    }

    private static Button CreateButton(
        string text,
        Action onClick,
        float width,
        float height,
        bool fixedWidth = true
    )
    {
        var button = new Button(() => onClick()) { text = text };
        if (fixedWidth)
            UiStyle.FixedWidth(button.style, width);
        button.style.height = height;
        button.style.flexGrow = 0f;
        button.style.flexShrink = 0f;
        button.style.unityFont = BppUiFont.Default;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.justifyContent = Justify.Center;
        button.style.alignItems = Align.Center;
        button.style.overflow = Overflow.Hidden;
        UiStyle.Padding(button.style, UiSpacing.None);
        button.style.backgroundColor = Colors.HistoryButtonBackground;
        button.style.color = Colors.White;
        UiStyle.Border(button.style, Borders.Thin, Colors.HistoryButtonBorder);
        UiStyle.Radius(button.style, Radii.Md);

        var textElement = button.Q<TextElement>();
        if (textElement != null && !ReferenceEquals(textElement, button))
        {
            textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            textElement.style.flexGrow = 1f;
            textElement.style.flexShrink = 1f;
            textElement.style.minWidth = 0f;
            textElement.style.whiteSpace = WhiteSpace.NoWrap;
            textElement.style.overflow = Overflow.Hidden;
            textElement.style.unityFont = BppUiFont.Default;
        }
        button.tooltip = text;
        return button;
    }

    private static void StyleButton(Button button, Color background, Color textColor)
    {
        button.style.backgroundColor = background;
        button.style.color = textColor;
        UiStyle.BorderColor(button.style, Colors.ButtonBorderFor(background));
    }
}
