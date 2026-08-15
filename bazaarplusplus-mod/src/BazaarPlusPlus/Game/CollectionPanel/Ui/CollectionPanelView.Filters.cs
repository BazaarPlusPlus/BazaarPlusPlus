#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop.EncounterPortraits;
using BazaarPlusPlus.GameInterop.Heroes;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.TagTypography;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

internal sealed partial class CollectionPanelView
{
    private const string HeroChipBadgeName = "bpp-collection-hero-chip-badge";
    private const string HeroChipGradientName = "bpp-collection-hero-chip-gradient";
    private const string HeroChipPortraitName = "bpp-collection-hero-chip-portrait";
    private const string HeroChipSelectionRingName = "bpp-collection-hero-chip-selection-ring";
    private const string SizeChipIconName = "bpp-collection-size-chip-icon";
    private const string SourceChipEncounterGradientName =
        "bpp-collection-source-chip-encounter-gradient";
    private const string SourceChipPortraitName = "bpp-collection-source-chip-portrait";
    private const string SourceChipSelectionRingName = "bpp-collection-source-chip-selection-ring";

    private static readonly CollectionPortraitFailureGate<
        EHero,
        CollectionPortraitReasonCode
    > HeroPortraitFailures = new();
    private static readonly CollectionPortraitFailureGate<
        Guid,
        CollectionPortraitReasonCode
    > EncounterPortraitFailures = new();

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
        {
            ApplyTierChipLayout();
            return;
        }
        ClearChipRow(_tierChips, _tierChipRow, keepFirst: false);
        var index = 0;
        foreach (var tier in tiers)
        {
            if (index > 0)
                _tierChipRow.Add(CreateChipGroupDivider());

            var chip = CreateChipButton(
                CollectionPanelText.Tier(tier),
                () => _commands.ToggleTier(tier),
                contentWidth: true
            );
            UiStyle.HorizontalPadding(chip.style, CurrentTierChipHorizontalPadding());
            StyleCollectionGroupChip(chip, Colors.CollectionChipBackground, TierTextColor(tier));
            _tierChips[tier] = chip;
            _tierChipRow.Add(chip);
            index++;
        }
        ApplyTierChipLayout();
    }

    private void ApplyTierChipLayout()
    {
        var horizontalPadding = CurrentTierChipHorizontalPadding();
        foreach (var chip in _tierChips.Values)
            UiStyle.HorizontalPadding(chip.style, horizontalPadding);
    }

    private static float CurrentTierChipHorizontalPadding() =>
        CollectionPanelText.IsChineseLanguage() ? UiSpacing.Lg : UiSpacing.Sm;

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
        {
            ApplySizeChipLayout(sizes);
            return;
        }
        ClearChipRow(_sizeChips, _sizeChipRow, keepFirst: false);
        var index = 0;
        foreach (var size in sizes)
        {
            if (index > 0)
                _sizeChipRow.Add(CreateChipGroupDivider());

            var chip = CreateSizeChipButton(size, () => _commands.ToggleSize(size));
            StyleCollectionGroupChip(
                chip,
                Colors.CollectionChipBackground,
                Colors.CollectionChipText
            );
            _sizeChips[size] = chip;
            _sizeChipRow.Add(chip);
            index++;
        }
        ApplySizeChipLayout(sizes);
    }

    private void ApplySizeChipLayout(IReadOnlyList<ECardSize> sizes)
    {
        if (_sizeChipRow == null)
            return;

        var isChinese = CollectionPanelText.IsChineseLanguage();
        var chipWidth = CurrentSizeChipWidth();
        if (isChinese)
        {
            var segmentWidth =
                sizes.Count * chipWidth
                + Mathf.Max(0, sizes.Count - 1) * Borders.Thin
                + Borders.Thin * 2f;
            UiStyle.FixedWidth(_sizeChipRow.style, segmentWidth);
            _sizeChipRow.style.flexBasis = segmentWidth;
        }
        else
        {
            _sizeChipRow.style.width = StyleKeyword.Auto;
            _sizeChipRow.style.minWidth = 0f;
            _sizeChipRow.style.maxWidth = StyleKeyword.Auto;
            _sizeChipRow.style.flexBasis = StyleKeyword.Auto;
        }
        var horizontalPadding = CurrentSizeChipHorizontalPadding();
        foreach (var chip in _sizeChips.Values)
        {
            if (isChinese)
            {
                UiStyle.FixedWidth(chip.style, chipWidth);
            }
            else
            {
                chip.style.width = StyleKeyword.Auto;
                chip.style.minWidth = 0f;
                chip.style.maxWidth = StyleKeyword.Auto;
            }
            UiStyle.HorizontalPadding(chip.style, horizontalPadding);
        }
    }

    private static float CurrentSizeChipWidth() =>
        Sizes.CollectionSizeChipWidth
        - (CollectionPanelText.IsChineseLanguage() ? 0f : UiSpacing.Sm);

    private static float CurrentSizeChipHorizontalPadding() =>
        CollectionPanelText.IsChineseLanguage() ? UiSpacing.Md : UiSpacing.Xs;

    private Button CreateSizeChipButton(ECardSize size, Action onClick)
    {
        var chip = CreateChipButton(string.Empty, onClick, contentWidth: true);
        UiStyle.HorizontalPadding(chip.style, CurrentSizeChipHorizontalPadding());
        // Custom content uses a deterministic width rather than Button.text measurement.
        if (CollectionPanelText.IsChineseLanguage())
            UiStyle.FixedWidth(chip.style, CurrentSizeChipWidth());
        chip.style.flexDirection = FlexDirection.Row;
        chip.style.alignItems = Align.Center;
        chip.style.justifyContent = Justify.Center;
        var icon = new VisualElement { name = SizeChipIconName, pickingMode = PickingMode.Ignore };
        UiStyle.FixedSize(icon.style, 18f, 14f);
        icon.style.marginRight = CollectionPanelText.IsChineseLanguage() ? UiSpacing.Xs : 0f;
        icon.style.display = CollectionPanelText.IsChineseLanguage()
            ? DisplayStyle.Flex
            : DisplayStyle.None;
        icon.Add(CreateSizeGlyph(size));
        var label = new Label(CollectionPanelText.Size(size))
        {
            name = SizeChipLabelName,
            pickingMode = PickingMode.Ignore,
        };
        label.style.fontSize = Sizes.CollectionTagFontSize;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.flexShrink = 0f;
        label.style.color = Colors.CollectionChipText;
        chip.Add(icon);
        chip.Add(label);
        return chip;
    }

    private static VisualElement CreateSizeGlyph(ECardSize size)
    {
        var (width, height) = size switch
        {
            ECardSize.Small => (6f, 10f),
            ECardSize.Large => (15f, 10f),
            _ => (10f, 10f),
        };
        var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
        glyph.style.position = Position.Absolute;
        glyph.style.left = (18f - width) / 2f;
        glyph.style.top = (14f - height) / 2f + 1f;
        UiStyle.FixedSize(glyph.style, width, height);
        glyph.style.backgroundColor = Colors.HistorySubtitleText;
        UiStyle.Radius(glyph.style, 1.5f);
        return glyph;
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
            EnsureKeywordChips(model.AvailableKeywordOptions);
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
                ApplyTagChipContent(chip, ResolveTagDisplay(captured));
                _tagChips[captured] = chip;
                _tagChipOrder.Add(captured);
                _tagChipRow.Add(chip);
            }
        }
    }

    private void EnsureKeywordChips(IReadOnlyList<CollectionKeywordFacetOption> options)
    {
        if (_keywordChipRow == null)
            return;
        if (!KeywordChipsMatch(options))
        {
            ClearKeywordFacetRow();
            var addedRelatedGroup = false;
            foreach (var option in options)
            {
                if (option.IsRelated && !addedRelatedGroup)
                {
                    _keywordChipRow.Add(CreateFacetGroupSpacer());
                    addedRelatedGroup = true;
                }
                var captured = option;
                var chip = CreateTagFacetChipButton(() => _commands.ToggleKeyword(captured));
                ApplyTagChipContent(chip, ResolveTagDisplay(captured), TagIconSize(captured));
                _keywordChips[captured] = chip;
                _keywordChipOrder.Add(captured);
                _keywordChipRow.Add(chip);
            }
        }
    }

    private static VisualElement CreateFacetGroupSpacer()
    {
        var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
        spacer.style.width = Length.Percent(100f);
        spacer.style.flexBasis = Length.Percent(100f);
        spacer.style.height = UiSpacing.Md;
        spacer.style.flexShrink = 0f;
        return spacer;
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

    private bool KeywordChipsMatch(IReadOnlyList<CollectionKeywordFacetOption> visible)
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
        _keywordChipRow?.Clear();
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
                if (i > 0)
                    sourceLine.style.marginTop = UiSpacing.Xs;
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
        foreach (var pair in _heroChips)
        {
            if (_heroChipIcons.TryGetValue(pair.Key, out var iconElement))
                ResizeHeroChip(pair.Value, iconElement, pair.Key, box);
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
        foreach (var pair in _sourceChips)
        {
            if (_sourceChipIcons.TryGetValue(pair.Key, out var iconElement))
                ResizeSourceChip(pair.Value, iconElement, box);
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

    private Button CreateChipButton(
        string text,
        Action onClick,
        bool fillRow = false,
        bool contentWidth = false
    )
    {
        var chip = CreateButton(
            text,
            onClick,
            fillRow || contentWidth ? 0f : Sizes.ChipMinWidth + 12f,
            Sizes.CollectionTagChipHeight,
            fixedWidth: !fillRow && !contentWidth
        );
        if (fillRow)
        {
            // Preserve the content-based flex basis so shorter labels yield room to longer ones.
            chip.style.flexGrow = 1f;
            chip.style.flexShrink = 1f;
        }
        else if (contentWidth)
        {
            chip.style.minWidth = 0f;
            chip.style.flexGrow = 0f;
            chip.style.flexShrink = 0f;
            UiStyle.HorizontalPadding(chip.style, UiSpacing.Md);
        }
        chip.style.marginRight = fillRow || contentWidth ? 0f : UiSpacing.Sm;
        chip.style.marginBottom = UiSpacing.Xs;
        var textElement = chip.Q<TextElement>();
        if (textElement != null)
            textElement.style.fontSize = Sizes.CollectionTagFontSize;
        StyleCollectionChip(chip, Colors.CollectionChipBackground, Colors.CollectionChipText);
        return chip;
    }

    private static Button CreateTagFacetChipButton(Action onClick)
    {
        var chip = CreateButton(
            string.Empty,
            onClick,
            0f,
            Sizes.CollectionTagChipHeight,
            fixedWidth: false
        );
        chip.style.minWidth = 0f;
        chip.style.flexDirection = FlexDirection.Row;
        chip.style.alignItems = Align.Center;
        chip.style.justifyContent = Justify.Center;
        UiStyle.HorizontalPadding(chip.style, UiSpacing.Sm);
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
        label.style.fontSize = Sizes.CollectionTagFontSize;
        label.style.unityFontStyleAndWeight = FontStyle.Normal;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.flexShrink = 1f;
        label.style.minWidth = 0f;
        label.style.maxWidth = Sizes.TagFacetChipMaxWidth;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        chip.Add(label);

        StyleCollectionChip(chip, Colors.CollectionChipBackground, Colors.CollectionChipText);
        return chip;
    }

    private static float TagIconSize(CollectionKeywordFacetOption option) =>
        option.Keyword == EHiddenTag.Lifesteal
            ? Sizes.CollectionLifestealTagIconSize
            : Sizes.TagChipIconSize;

    private static void ApplyTagChipContent(
        Button chip,
        NativeTagDisplay display,
        float iconSize = Sizes.TagChipIconSize
    )
    {
        var label = chip.Q<Label>(TagChipLabelName);
        if (label != null)
            label.text = StablePanelText.Compact(display.Label, 24);
        chip.tooltip = display.Label;

        var icon = chip.Q<VisualElement>(TagChipIconName);
        if (icon == null)
            return;

        UiStyle.FixedSize(icon.style, iconSize, iconSize);

        var outcome = KeywordIconSpriteProvider.Resolve(display.IconName);
        if (outcome.IsDegraded)
        {
            BppLog.WarnEvent(
                CollectionPanelLogEvents.KeywordIconDegraded,
                outcome.Exception!,
                CollectionPanelLogEvents.KeywordIconDegradedReasonCode.Bind(
                    CollectionTypographyReasonCode.IconResolveException
                ),
                CollectionPanelLogEvents.KeywordIconDegradedIconName.Bind(outcome.IconName)
            );
        }
        if (outcome.Sprite != null)
        {
            icon.style.backgroundImage = new StyleBackground(outcome.Sprite);
            icon.style.display = DisplayStyle.Flex;
            icon.MarkDirtyRepaint();
            return;
        }

        icon.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
        icon.style.display = DisplayStyle.None;
    }

    private static NativeTagDisplay ResolveTagDisplay(ECardTag tag)
    {
        var display = NativeTagTypography.Resolve(tag);
        ReportTagTypographyFailure();
        return display;
    }

    private static NativeTagDisplay ResolveTagDisplay(EHiddenTag tag)
    {
        var display = NativeTagTypography.Resolve(tag);
        ReportTagTypographyFailure();
        return display;
    }

    private static NativeTagDisplay ResolveTagDisplay(CollectionKeywordFacetOption option)
    {
        if (option.Keyword.HasValue)
            return ResolveTagDisplay(option.Keyword.Value);

        var display = option.Mechanic switch
        {
            CollectionMechanic.Multicast => NativeTagTypography.Resolve(EHiddenTag.Multicast),
            CollectionMechanic.Destroy => NativeTagTypography.Resolve("Destroy"),
            _ => NativeTagTypography.Resolve(option.Mechanic?.ToString() ?? string.Empty),
        };
        ReportTagTypographyFailure();
        return display;
    }

    private static bool IsKeywordOptionSelected(
        CollectionKeywordFacetOption option,
        CollectionPanelViewModel model
    ) =>
        option.Keyword.HasValue
            ? model.SelectedKeywords.Contains(option.Keyword.Value)
            : option.Mechanic.HasValue && model.SelectedMechanics.Contains(option.Mechanic.Value);

    private static void ReportTagTypographyFailure()
    {
        if (!NativeTagTypography.TryTakeFailure(out var failure))
            return;
        var reasonCode =
            failure.Reason == NativeTagTypographyFailureReason.ConfigurationMethodUnavailable
                ? CollectionTypographyReasonCode.ConfigurationMethodUnavailable
                : CollectionTypographyReasonCode.ConfigurationInvocationException;
        var fields = new[]
        {
            CollectionPanelLogEvents.TagTypographyDegradedReasonCode.Bind(reasonCode),
        };
        if (failure.Exception == null)
            BppLog.WarnEvent(CollectionPanelLogEvents.TagTypographyDegraded, fields);
        else
            BppLog.WarnEvent(
                CollectionPanelLogEvents.TagTypographyDegraded,
                failure.Exception,
                fields
            );
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
        StyleHeroChip(chip);

        var icon = CreateHeroChipIcon(hero);
        chip.Add(icon);
        chip.Add(CreateHeroSelectionRing());
        BindHeroChipInteraction(chip, icon);
        ResizeHeroChip(chip, icon, hero, CurrentHeroChipBox());

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
        StyleCollectionChip(chip, Colors.CollectionChipBackground, Colors.CollectionChipText);
        UiStyle.Radius(chip.style, Radii.CollectionPortraitChip);

        var icon = CreateSourceChipIcon(source.DisplayName);
        chip.Add(icon);
        chip.Add(CreateSourceSelectionRing());
        var box = CurrentSourceChipBox();
        ResizeSourceChip(chip, icon, box);

        _sourceChipIcons[source.SourceKey] = icon;
        LoadSourceChipIcon(source.SourceKey, source.RepresentativeTemplateId, icon);
        return chip;
    }

    private static VisualElement CreateHeroChipIcon(EHero hero)
    {
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        StretchPortraitToParent(icon);
        var portrait = CreatePortraitLayer(HeroChipPortraitName);

        if (HeroPortraitSpriteProvider.IsRenderableHero(hero))
        {
            icon.Add(CreateHeroGradient(hero));
            icon.Add(portrait);
            AddHeroBadgeFallback(icon, hero, Sizes.HeroChipIconSize);
        }
        else
        {
            icon.Add(portrait);
            AddCommonHeroGlyph(icon, Sizes.HeroChipIconSize);
        }

        return icon;
    }

    private static VisualElement CreateHeroSelectionRing()
    {
        var ring = new VisualElement
        {
            name = HeroChipSelectionRingName,
            pickingMode = PickingMode.Ignore,
        };
        StretchPortraitToParent(ring);
        UiStyle.Border(ring.style, Borders.Accent, Colors.CollectionChipBorder);
        UiStyle.Radius(ring.style, Radii.CollectionPortraitChip);
        ring.style.display = DisplayStyle.None;
        return ring;
    }

    private static VisualElement CreateSourceChipIcon(string displayName)
    {
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        StretchPortraitToParent(icon);
        icon.Add(CreateSourceEncounterGradient());
        icon.Add(CreatePortraitLayer(SourceChipPortraitName));

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

    private static VisualElement CreateSourceSelectionRing()
    {
        var ring = new VisualElement
        {
            name = SourceChipSelectionRingName,
            pickingMode = PickingMode.Ignore,
        };
        StretchPortraitToParent(ring);
        UiStyle.Border(ring.style, Borders.Accent, Colors.CollectionChipSelectedBorder);
        UiStyle.Radius(ring.style, Radii.CollectionPortraitChip);
        ring.style.display = DisplayStyle.None;
        return ring;
    }

    private static void RefreshSourceSelectionRing(Button chip, bool selected)
    {
        var ring = chip.Q<VisualElement>(SourceChipSelectionRingName);
        if (ring == null)
            return;

        ring.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
        UiStyle.BorderColor(ring.style, Colors.CollectionChipSelectedBorder);
    }

    private float CurrentSourceChipBox() =>
        _appliedSourceChipBox > 0f ? _appliedSourceChipBox : Sizes.SourceChipMinSize;

    private float CurrentHeroChipBox() =>
        _appliedHeroChipBox > 0f ? _appliedHeroChipBox : Sizes.HeroChipButtonSize;

    private static void ResizeHeroChip(Button chip, VisualElement icon, EHero hero, float box)
    {
        chip.style.width = box;
        chip.style.minWidth = box;
        chip.style.maxWidth = box;
        chip.style.height = box;
        chip.style.minHeight = box;
        chip.style.maxHeight = box;
        ResizeHeroIcon(icon);
        if (HeroPortraitSpriteProvider.IsRenderableHero(hero))
            ResizeHeroBadge(icon, box);
        else
            AddCommonHeroGlyph(icon, box);
    }

    private static void ResizeSourceChip(Button chip, VisualElement icon, float box)
    {
        chip.style.width = box;
        chip.style.minWidth = box;
        chip.style.maxWidth = box;
        chip.style.height = box;
        chip.style.minHeight = box;
        chip.style.maxHeight = box;
        ResizeSourceIcon(icon);
    }

    private static void ResizeHeroIcon(VisualElement icon)
    {
        StretchPortraitToParent(icon);
        StretchPortraitToParentIfPresent(icon, HeroChipPortraitName);
        StretchPortraitToParentIfPresent(icon, HeroChipGradientName);
    }

    private static void ResizeSourceIcon(VisualElement icon)
    {
        StretchPortraitToParent(icon);
        StretchPortraitToParentIfPresent(icon, SourceChipEncounterGradientName);
        StretchPortraitToParentIfPresent(icon, SourceChipPortraitName);
    }

    private static VisualElement CreatePortraitLayer(string name)
    {
        var portrait = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
        StretchPortraitToParent(portrait);
        UiStyle.Padding(portrait.style, UiSpacing.None);
        portrait.style.marginBottom = 0f;
        portrait.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        portrait.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
        portrait.style.backgroundPositionX = new BackgroundPosition(
            BackgroundPositionKeyword.Center
        );
        portrait.style.backgroundPositionY = new BackgroundPosition(
            BackgroundPositionKeyword.Bottom
        );
        return portrait;
    }

    private static void StretchPortraitToParentIfPresent(VisualElement parent, string name)
    {
        var portrait = parent.Q<VisualElement>(name);
        if (portrait != null)
            StretchPortraitToParent(portrait);
    }

    private static void StretchPortraitToParent(VisualElement portrait)
    {
        portrait.style.position = Position.Absolute;
        portrait.style.left = 0f;
        portrait.style.right = 0f;
        portrait.style.top = 0f;
        portrait.style.bottom = 0f;
        portrait.style.width = StyleKeyword.Auto;
        portrait.style.height = StyleKeyword.Auto;
    }

    private static void AddCommonHeroGlyph(VisualElement icon, float iconSize)
    {
        icon.Clear();
        icon.style.backgroundColor = Colors.HistoryStatusBackground;
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

    private static void AddHeroBadgeFallback(VisualElement icon, EHero hero, float iconSize)
    {
        var style = HeroVisual.Resolve(hero.ToString());

        var badge = CreateLabel(HeroBadgeFontSize(iconSize), FontStyle.Bold, style.Text);
        badge.name = HeroChipBadgeName;
        badge.text = style.ShortCode;
        badge.pickingMode = PickingMode.Ignore;
        badge.style.position = Position.Absolute;
        badge.style.left = 0f;
        badge.style.right = 0f;
        badge.style.top = 0f;
        badge.style.bottom = 0f;
        badge.style.unityTextAlign = TextAnchor.MiddleCenter;
        icon.Add(badge);
    }

    private static VisualElement CreateHeroGradient(EHero hero)
    {
        var gradient = new VisualElement
        {
            name = HeroChipGradientName,
            pickingMode = PickingMode.Ignore,
        };
        StretchPortraitToParent(gradient);
        var themeColor = DarkenPortraitThemeColor(HeroVisual.Resolve(hero.ToString()).Background);
        var state = new HeroGradientVisualState(gradient, themeColor);
        gradient.userData = state;
        gradient.generateVisualContent += context => DrawHeroGradient(context, gradient, state);
        return gradient;
    }

    private static VisualElement CreateSourceEncounterGradient()
    {
        var gradient = new VisualElement
        {
            name = SourceChipEncounterGradientName,
            pickingMode = PickingMode.Ignore,
        };
        StretchPortraitToParent(gradient);
        gradient.style.display = DisplayStyle.None;
        var themeColor = Colors.CollectionChipSelectedBorder;
        var state = new HeroGradientVisualState(gradient, themeColor);
        state.SetAppearance(1.08f, 0.98f);
        gradient.userData = state;
        gradient.generateVisualContent += context => DrawHeroGradient(context, gradient, state);
        return gradient;
    }

    private static void RefreshSourceEncounterHighlight(VisualElement icon, bool highlighted)
    {
        var gradient = icon.Q<VisualElement>(SourceChipEncounterGradientName);
        if (gradient == null)
            return;

        gradient.style.display = highlighted ? DisplayStyle.Flex : DisplayStyle.None;
        if (highlighted)
            gradient.MarkDirtyRepaint();
    }

    private static void DrawHeroGradient(
        MeshGenerationContext context,
        VisualElement gradient,
        HeroGradientVisualState state
    )
    {
        var rect = gradient.contentRect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        var themeColor = state.CurrentColor;
        var topColor = new Color(themeColor.r, themeColor.g, themeColor.b, 0f);
        var bottomColor = new Color(themeColor.r, themeColor.g, themeColor.b, state.BottomAlpha);
        var mesh = context.Allocate(4, 6);
        mesh.SetNextVertex(
            new Vertex
            {
                position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ),
                tint = topColor,
            }
        );
        mesh.SetNextVertex(
            new Vertex
            {
                position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ),
                tint = topColor,
            }
        );
        mesh.SetNextVertex(
            new Vertex
            {
                position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ),
                tint = bottomColor,
            }
        );
        mesh.SetNextVertex(
            new Vertex
            {
                position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ),
                tint = bottomColor,
            }
        );
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(1);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(3);
    }

    private static Color DarkenPortraitThemeColor(Color color) =>
        new(color.r * 0.42f, color.g * 0.42f, color.b * 0.42f, 1f);

    private static void BindHeroChipInteraction(Button chip, VisualElement icon)
    {
        var gradient = icon.Q<VisualElement>(HeroChipGradientName);
        if (gradient == null)
            return;

        if (gradient.userData is not HeroGradientVisualState gradientState)
            return;

        var state = new HeroChipInteractionState(gradientState);
        chip.userData = state;
        chip.RegisterCallback<MouseEnterEvent>(_ =>
        {
            state.Hovered = true;
            state.Refresh();
        });
        chip.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            state.Hovered = false;
            state.Pressed = false;
            state.Refresh();
        });
        chip.RegisterCallback<MouseDownEvent>(_ =>
        {
            state.Pressed = true;
            state.Refresh();
        });
        chip.RegisterCallback<MouseUpEvent>(_ =>
        {
            state.Pressed = false;
            state.Refresh();
        });
        state.Refresh();
    }

    private static void RefreshHeroChipInteraction(Button chip, bool selected)
    {
        if (chip.userData is HeroChipInteractionState state)
        {
            state.Selected = selected;
            state.Refresh();
        }
    }

    private sealed class HeroChipInteractionState
    {
        private readonly HeroGradientVisualState _gradient;

        internal HeroChipInteractionState(HeroGradientVisualState gradient) => _gradient = gradient;

        internal bool Hovered { get; set; }
        internal bool Pressed { get; set; }
        internal bool Selected { get; set; }

        internal void Refresh()
        {
            if (Pressed)
                _gradient.SetAppearance(1.14f, 0.94f);
            else if (Selected)
                _gradient.SetAppearance(1.32f, 1f);
            else if (Hovered)
                _gradient.SetAppearance(1.08f, 0.90f);
            else
                _gradient.SetAppearance(1f, 0.84f);
        }
    }

    private sealed class HeroGradientVisualState
    {
        private readonly VisualElement _gradient;
        private readonly Color _themeColor;
        private float _colorScale = 1f;

        internal HeroGradientVisualState(VisualElement gradient, Color themeColor)
        {
            _gradient = gradient;
            _themeColor = themeColor;
        }

        internal float BottomAlpha { get; private set; } = 0.84f;

        internal Color CurrentColor =>
            new(
                Mathf.Clamp01(_themeColor.r * _colorScale),
                Mathf.Clamp01(_themeColor.g * _colorScale),
                Mathf.Clamp01(_themeColor.b * _colorScale),
                1f
            );

        internal void SetAppearance(float colorScale, float bottomAlpha)
        {
            _colorScale = colorScale;
            BottomAlpha = bottomAlpha;
            _gradient.MarkDirtyRepaint();
        }
    }

    private static void ResizeHeroBadge(VisualElement icon, float iconSize)
    {
        var badge = icon.Q<Label>(HeroChipBadgeName);
        if (badge != null)
            badge.style.fontSize = HeroBadgeFontSize(iconSize);
    }

    private static int HeroBadgeFontSize(float iconSize) =>
        Mathf.Max(Sizes.FontTiny, Mathf.RoundToInt(iconSize * 0.25f));

    private static void LoadHeroChipIcon(EHero hero, VisualElement icon)
    {
        icon.userData = hero;

        if (HeroPortraitSpriteProvider.TryGetCached(hero, out var cached))
        {
            ReportHeroPortraitOutcome(hero, cached);
            ApplyHeroChipIcon(icon, cached?.Sprite);
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
            ReportEncounterPortraitOutcome(representativeTemplateId, cached);
            ApplySourceChipIcon(icon, cached?.Sprite);
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
        var outcome = await HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(hero);
        if (!Equals(icon.userData, hero))
            return;
        ReportHeroPortraitOutcome(hero, outcome);
        ApplyHeroChipIcon(icon, outcome?.Sprite);
    }

    private static async System.Threading.Tasks.Task ApplySourceChipIconWhenLoadedAsync(
        string sourceKey,
        Guid representativeTemplateId,
        VisualElement icon
    )
    {
        var outcome = await EncounterPortraitSpriteProvider.LoadPortraitAsync(
            representativeTemplateId
        );
        if (!Equals(icon.userData, sourceKey))
            return;
        ReportEncounterPortraitOutcome(representativeTemplateId, outcome);
        ApplySourceChipIcon(icon, outcome?.Sprite);
    }

    private static void ReportHeroPortraitOutcome(EHero hero, HeroPortraitLoadOutcome? outcome)
    {
        if (outcome == null)
            return;
        if (!outcome.IsDegraded)
        {
            HeroPortraitFailures.Clear(hero);
            return;
        }
        var reasonCode = outcome.Reason switch
        {
            HeroPortraitFailureReason.CollectionManagerUnavailable =>
                CollectionPortraitReasonCode.CollectionManagerUnavailable,
            HeroPortraitFailureReason.DefaultSkinUnavailable =>
                CollectionPortraitReasonCode.DefaultSkinUnavailable,
            HeroPortraitFailureReason.PortraitUnavailable =>
                CollectionPortraitReasonCode.PortraitUnavailable,
            _ => CollectionPortraitReasonCode.LoadException,
        };
        if (!HeroPortraitFailures.ShouldReport(hero, reasonCode))
            return;
        if (outcome.Reason == HeroPortraitFailureReason.PortraitUnavailable)
        {
            BppLog.DebugEvent(
                CollectionPanelLogEvents.HeroPortraitFallbackObserved,
                () =>
                    [
                        CollectionPanelLogEvents.HeroPortraitFallbackHero.Bind(hero),
                        CollectionPanelLogEvents.HeroPortraitFallbackReasonCode.Bind(reasonCode),
                    ]
            );
            return;
        }

        var fields = new[]
        {
            CollectionPanelLogEvents.HeroPortraitDegradedHero.Bind(hero),
            CollectionPanelLogEvents.HeroPortraitDegradedReasonCode.Bind(reasonCode),
        };
        if (outcome.Exception == null)
            BppLog.WarnEvent(CollectionPanelLogEvents.HeroPortraitDegraded, fields);
        else
            BppLog.WarnEvent(
                CollectionPanelLogEvents.HeroPortraitDegraded,
                outcome.Exception,
                fields
            );
    }

    private static void ReportEncounterPortraitOutcome(
        Guid templateId,
        EncounterPortraitLoadOutcome? outcome
    )
    {
        if (outcome == null)
            return;
        if (!outcome.IsDegraded)
        {
            EncounterPortraitFailures.Clear(templateId);
            return;
        }
        var reasonCode = outcome.Reason switch
        {
            EncounterPortraitFailureReason.ArtKeyUnavailable =>
                CollectionPortraitReasonCode.ArtKeyUnavailable,
            EncounterPortraitFailureReason.AssetLoaderUnavailable =>
                CollectionPortraitReasonCode.AssetLoaderUnavailable,
            EncounterPortraitFailureReason.EncounterAssetUnavailable =>
                CollectionPortraitReasonCode.EncounterAssetUnavailable,
            EncounterPortraitFailureReason.PortraitUnavailable =>
                CollectionPortraitReasonCode.PortraitUnavailable,
            _ => CollectionPortraitReasonCode.LoadException,
        };
        if (!EncounterPortraitFailures.ShouldReport(templateId, reasonCode))
            return;
        var fields = new[]
        {
            CollectionPanelLogEvents.EncounterPortraitDegradedTemplateId.Bind(templateId),
            CollectionPanelLogEvents.EncounterPortraitDegradedReasonCode.Bind(reasonCode),
            CollectionPanelLogEvents.EncounterPortraitDegradedArtKey.Bind(outcome.ArtKey),
        };
        if (outcome.Exception == null)
            BppLog.WarnEvent(CollectionPanelLogEvents.EncounterPortraitDegraded, fields);
        else
            BppLog.WarnEvent(
                CollectionPanelLogEvents.EncounterPortraitDegraded,
                outcome.Exception,
                fields
            );
    }

    private static void ApplyHeroChipIcon(VisualElement icon, Sprite? sprite)
    {
        var badge = icon.Q<Label>(HeroChipBadgeName);
        var portrait = icon.Q<VisualElement>(HeroChipPortraitName);
        if (sprite == null)
        {
            if (portrait != null)
                portrait.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
            if (badge != null)
                badge.style.display = DisplayStyle.Flex;
            return;
        }

        if (portrait != null)
            portrait.style.backgroundImage = new StyleBackground(sprite);
        if (badge != null)
            badge.style.display = DisplayStyle.None;
        portrait?.MarkDirtyRepaint();
    }

    private static void ApplySourceChipIcon(VisualElement icon, Sprite? sprite)
    {
        var initials = icon.Q<Label>(SourceChipInitialsName);
        var portrait = icon.Q<VisualElement>(SourceChipPortraitName);
        if (sprite == null)
        {
            if (portrait != null)
                portrait.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
            if (initials != null)
                initials.style.display = DisplayStyle.Flex;
            return;
        }

        if (portrait != null)
            portrait.style.backgroundImage = new StyleBackground(sprite);
        if (initials != null)
            initials.style.display = DisplayStyle.None;
        portrait?.MarkDirtyRepaint();
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
    // state keeps the blue highlight regardless so selection always reads the same way.
    private static void RefreshChip(Button chip, bool selected, Color? unselectedTextColor = null)
    {
        StyleCollectionChip(
            chip,
            selected ? Colors.CollectionChipSelectedBackground : Colors.CollectionChipBackground,
            selected
                ? Colors.CollectionChipSelectedText
                : unselectedTextColor ?? Colors.CollectionChipText,
            selected
        );
    }

    private static void RefreshTierChip(ETier tier, Button chip, bool selected)
    {
        var textColor = TierTextColor(tier);
        StyleCollectionGroupChip(
            chip,
            selected ? Colors.CollectionChipSelectedBackground : Colors.CollectionChipBackground,
            textColor,
            selected
        );
    }

    private static void RefreshSizeChip(Button chip, bool selected) =>
        StyleCollectionGroupChip(
            chip,
            selected ? Colors.CollectionChipSelectedBackground : Colors.CollectionChipBackground,
            selected ? Colors.CollectionChipSelectedText : Colors.CollectionChipText,
            selected
        );

    private static void RefreshSourceChip(Button chip, bool selected)
    {
        RefreshChip(chip, selected);
        UiStyle.Border(
            chip.style,
            selected ? Borders.None : Borders.Thin,
            selected ? Colors.Clear : Colors.CollectionChipBorder
        );
    }

    private static void RefreshSortChip(Button chip, bool selected)
    {
        UiStyle.FixedWidth(
            chip.style,
            selected ? CurrentSortActiveWidth() : CurrentSortInactiveWidth()
        );
        StyleCollectionGroupChip(
            chip,
            selected ? Colors.CollectionChipSelectedBackground : Colors.CollectionChipBackground,
            selected ? Colors.CollectionChipSelectedText : Colors.CollectionChipText,
            selected
        );
        var icon = chip.Q<VisualElement>(SortButtonIconName);
        if (icon != null)
            icon.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void ApplySortGroupLayout(CollectionSortPriority priority)
    {
        if (_sortQualityButton?.parent is not VisualElement group)
            return;

        var qualityWidth =
            priority == CollectionSortPriority.Quality
                ? CurrentSortActiveWidth()
                : CurrentSortInactiveWidth();
        var sizeWidth =
            priority == CollectionSortPriority.Size
                ? CurrentSortActiveWidth()
                : CurrentSortInactiveWidth();
        UiStyle.FixedWidth(group.style, qualityWidth + sizeWidth);
        var divider = group.Q<VisualElement>(FacetChoiceDividerName);
        if (divider != null)
            divider.style.left = qualityWidth;
    }

    private static void SetSortButtonText(Button button, string text)
    {
        var label = button.Q<Label>(SortButtonLabelName);
        if (label != null)
            label.text = text;
        else
            button.text = text;
    }

    private static float CurrentSortActiveWidth() =>
        CollectionPanelText.IsChineseLanguage()
            ? Sizes.CollectionSortActiveWidth
            : Sizes.CollectionSortEnglishActiveWidth;

    private static float CurrentSortInactiveWidth() =>
        CollectionPanelText.IsChineseLanguage()
            ? Sizes.CollectionSortInactiveWidth
            : Sizes.CollectionSortEnglishInactiveWidth;

    private static void StyleCollectionGroupChip(
        Button chip,
        Color background,
        Color textColor,
        bool selected = false
    )
    {
        StyleCollectionChip(chip, background, textColor, selected);
        UiStyle.BorderWidth(chip.style, Borders.None);
        UiStyle.Radius(chip.style, 0f);
        chip.style.marginRight = 0f;
        chip.style.marginBottom = 0f;
    }

    private static Color TierTextColor(ETier tier) =>
        tier switch
        {
            ETier.Bronze => Colors.FromRgb(222, 150, 110, 1f),
            ETier.Silver => Colors.FromRgb(192, 192, 192, 1f),
            ETier.Gold => Colors.FromRgb(255, 215, 0, 1f),
            ETier.Diamond => Colors.FromRgb(0, 255, 255, 1f),
            ETier.Legendary => Colors.FromRgb(255, 69, 0, 1f),
            _ => Colors.HistoryChipText,
        };

    private static void RefreshTextMatchModeControl(
        VisualElement? control,
        CollectionFacetMatchMode mode
    )
    {
        if (control == null)
            return;

        var any = control.Q<Button>(FacetMatchAnyName);
        var all = control.Q<Button>(FacetMatchAllName);
        if (any == null || all == null)
            return;

        any.text = CollectionPanelText.FacetMatchMode(CollectionFacetMatchMode.Any);
        all.text = CollectionPanelText.FacetMatchMode(CollectionFacetMatchMode.All);
        StyleTextMatchModeButton(any, mode == CollectionFacetMatchMode.Any);
        StyleTextMatchModeButton(all, mode == CollectionFacetMatchMode.All);
    }

    private static void StyleTextMatchModeButton(Button button, bool selected)
    {
        button.style.backgroundColor = Color.clear;
        UiStyle.BorderWidth(button.style, Borders.None);
        button.style.color = selected
            ? Colors.CollectionFilterTitleText
            : Colors.WithAlpha(Colors.CollectionFilterTitleText, 0.48f);
    }

    private void RefreshAllHeroesButton(bool selected)
    {
        if (_allHeroesButton == null)
            return;

        StyleTextMatchModeButton(_allHeroesButton, selected);
    }

    private static void RefreshFacetChoiceControl(
        VisualElement? control,
        bool firstSelected,
        string firstText,
        string secondText,
        int fontSize,
        bool slanted
    )
    {
        if (control == null)
            return;

        var first = control.Q<Button>(FacetMatchAnyName);
        var second = control.Q<Button>(FacetMatchAllName);
        if (first == null || second == null)
            return;

        SetFacetChoiceText(first, firstText);
        SetFacetChoiceText(second, secondText);
        StyleFacetMatchModeSegment(
            first,
            firstSelected,
            left: true,
            fontSize: fontSize,
            slanted: slanted
        );
        StyleFacetMatchModeSegment(
            second,
            !firstSelected,
            left: false,
            fontSize: fontSize,
            slanted: slanted
        );
    }

    private static void SetFacetChoiceText(Button button, string text)
    {
        var label = button.Q<Label>(FacetMatchLabelName);
        if (label != null)
            label.text = text;
        else
            button.text = text;
    }

    // Always visible; the face shows the effective day number and highlights when the day
    // participates in filtering (blue = on, chip background = off).
    private void RefreshDayToggle(int? day, bool active)
    {
        if (_dayToggleButton == null)
            return;

        if (_dayToggleCaption != null)
            _dayToggleCaption.text = CollectionPanelText.DayCaption();
        if (_dayToggleValue != null)
            _dayToggleValue.text =
                day?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        ApplyDayToggleLayout(CollectionPanelText.IsChineseLanguage());
        StyleCollectionChip(
            _dayToggleButton,
            active ? Colors.CollectionChipSelectedBackground : Colors.CollectionChipBackground,
            active ? Colors.CollectionChipSelectedText : Colors.CollectionChipText,
            active
        );
    }

    private void ApplyDayToggleLayout(bool isChinese)
    {
        if (_dayToggleContent == null || _dayToggleCaption == null || _dayToggleValue == null)
            return;

        _dayToggleContent.style.flexDirection = FlexDirection.Column;
        _dayToggleContent.style.alignItems = Align.Center;
        _dayToggleContent.style.justifyContent = Justify.Center;
        _dayToggleContent.style.left = 0f;
        _dayToggleCaption.style.fontSize = Sizes.FontTiny;
        _dayToggleCaption.style.marginBottom = isChinese ? -3f : -2f;
        _dayToggleCaption.style.marginRight = 0f;
        _dayToggleCaption.style.width = StyleKeyword.Auto;
        _dayToggleCaption.style.height = StyleKeyword.Auto;
        _dayToggleCaption.style.whiteSpace = WhiteSpace.NoWrap;
        _dayToggleCaption.style.unityTextAlign = TextAnchor.MiddleCenter;
        _dayToggleCaption.style.flexShrink = 0f;
        _dayToggleValue.style.fontSize = isChinese ? Sizes.FontButton + 1 : Sizes.FontButton;
        _dayToggleValue.style.height = StyleKeyword.Auto;
        _dayToggleValue.style.unityTextAlign = TextAnchor.MiddleCenter;
        _dayToggleValue.style.flexShrink = 0f;
    }

    private void RefreshHeroChip(EHero hero, Button chip, bool selected)
    {
        StyleHeroChip(chip);
        var ring = chip.Q<VisualElement>(HeroChipSelectionRingName);
        if (ring != null)
        {
            ring.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
            UiStyle.BorderColor(ring.style, HeroVisual.Resolve(hero.ToString()).Background);
        }
        RefreshHeroChipInteraction(chip, selected);
    }

    private static void StyleHeroChip(Button chip)
    {
        UiHover.ApplyButtonPalette(
            chip,
            Colors.CollectionChipBackground,
            Colors.CollectionChipText,
            Colors.CollectionChipBorder,
            Colors.CollectionChipBorder,
            Colors.CollectionChipBackground,
            Colors.CollectionChipBackground
        );
        UiStyle.BorderWidth(chip.style, Borders.Thin);
        UiStyle.Radius(chip.style, Radii.CollectionPortraitChip);
    }

    private static Label CreateLabel(int fontSize, FontStyle fontStyle, Color color)
    {
        var label = new Label();
        label.style.fontSize = fontSize;
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
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.justifyContent = Justify.Center;
        button.style.alignItems = Align.Center;
        button.style.overflow = Overflow.Hidden;
        button.style.whiteSpace = WhiteSpace.NoWrap;
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
        }
        button.tooltip = text;
        UiHover.ApplyButtonPalette(button, Colors.HistoryButtonBackground, Colors.White);
        return button;
    }

    private static void StyleButton(Button button, Color background, Color textColor)
    {
        UiHover.ApplyButtonPalette(button, background, textColor);
    }

    private static void StyleCollectionChip(
        Button button,
        Color background,
        Color textColor,
        bool selected = false
    )
    {
        button.style.fontSize = Sizes.CollectionTagFontSize;
        UiHover.ApplyButtonPalette(
            button,
            background,
            textColor,
            selected ? Colors.CollectionChipSelectedBorder : Colors.CollectionChipBorder,
            selected ? Colors.CollectionChipSelectedHoverBorder : Colors.CollectionChipHoverBorder,
            selected
                ? Colors.CollectionChipSelectedHoverBackground
                : Colors.CollectionChipHoverBackground,
            selected
                ? Colors.CollectionChipSelectedPressedBackground
                : Colors.CollectionChipPressedBackground
        );
        UiStyle.Radius(button.style, Radii.CollectionChip);
    }

    private static void StyleFacetMatchModeSegment(
        Button button,
        bool selected,
        bool left,
        int fontSize,
        bool slanted
    )
    {
        button.style.fontSize = fontSize;
        button.style.backgroundColor = slanted ? Color.clear : Colors.CollectionChipBackground;
        UiStyle.BorderWidth(button.style, Borders.None);
        UiStyle.Radius(button.style, 0f);

        if (button.userData is not FacetMatchModeSegmentState state)
        {
            state = new FacetMatchModeSegmentState(button, left, fontSize, slanted);
            button.userData = state;
            if (slanted)
                button.generateVisualContent += context =>
                    DrawFacetMatchModeSegment(context, state);
            button.RegisterCallback<MouseEnterEvent>(_ =>
            {
                state.Hovered = true;
                state.Refresh();
            });
            button.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                state.Hovered = false;
                state.Pressed = false;
                state.Refresh();
            });
            button.RegisterCallback<MouseDownEvent>(_ =>
            {
                state.Pressed = true;
                state.Refresh();
            });
            button.RegisterCallback<MouseUpEvent>(_ =>
            {
                state.Pressed = false;
                state.Refresh();
            });
        }

        state.Selected = selected;
        state.Refresh();
    }

    private static void DrawFacetMatchModeSegment(
        MeshGenerationContext context,
        FacetMatchModeSegmentState state
    )
    {
        var rect = state.Button.contentRect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        var background = state.BackgroundColor;
        var cut = Mathf.Min(4f, rect.width * 0.08f);
        var topLeft = state.Left ? rect.xMin : rect.xMin + cut;
        var bottomRight = state.Left ? rect.xMax - cut : rect.xMax;
        var mesh = context.Allocate(4, 6);
        mesh.SetNextVertex(
            new Vertex
            {
                position = new Vector3(topLeft, rect.yMin, Vertex.nearZ),
                tint = background,
            }
        );
        mesh.SetNextVertex(
            new Vertex
            {
                position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ),
                tint = background,
            }
        );
        mesh.SetNextVertex(
            new Vertex
            {
                position = new Vector3(bottomRight, rect.yMax, Vertex.nearZ),
                tint = background,
            }
        );
        mesh.SetNextVertex(
            new Vertex
            {
                position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ),
                tint = background,
            }
        );
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(1);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(3);
    }

    private sealed class FacetMatchModeSegmentState
    {
        internal FacetMatchModeSegmentState(Button button, bool left, int fontSize, bool slanted)
        {
            Button = button;
            Left = left;
            TextLabel = button.Q<Label>(FacetMatchLabelName);
            FontSize = fontSize;
            Slanted = slanted;
        }

        internal Button Button { get; }

        internal bool Left { get; }

        internal Label? TextLabel { get; }

        internal int FontSize { get; }

        internal bool Slanted { get; }

        internal bool Selected { get; set; }

        internal bool Hovered { get; set; }

        internal bool Pressed { get; set; }

        internal Color BackgroundColor
        {
            get
            {
                if (Pressed)
                {
                    return Selected
                        ? Colors.CollectionChipSelectedPressedBackground
                        : Colors.CollectionChipPressedBackground;
                }

                if (Hovered)
                {
                    return Selected
                        ? Colors.CollectionChipSelectedHoverBackground
                        : Colors.CollectionChipHoverBackground;
                }

                return Selected
                    ? Colors.CollectionChipSelectedBackground
                    : Colors.CollectionChipBackground;
            }
        }

        internal void Refresh()
        {
            Button.style.backgroundColor = Slanted ? Color.clear : BackgroundColor;
            Button.style.color = Selected
                ? Colors.CollectionChipSelectedText
                : Colors.CollectionChipText;
            if (TextLabel != null)
            {
                TextLabel.style.fontSize = FontSize;
                TextLabel.style.color = Selected
                    ? Colors.CollectionChipSelectedText
                    : Colors.CollectionChipText;
            }
            Button.MarkDirtyRepaint();
        }
    }
}
