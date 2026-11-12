#nullable enable
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

// The merchant and trainer cells. The merchants of a folding catalog group share one cell
// (CollectionSourceFold) with a count badge; a click on it opens a strip of its members under
// its line, and a click on a member selects that member. The selection always names a member.
internal sealed partial class CollectionPanelView
{
    private const string SourceChipCountName = "bpp-collection-source-chip-count";

    private void EnsureSourceChips(IReadOnlyList<CollectionSourceGroup> groups)
    {
        if (_sourceChipRow == null)
            return;
        if (SourceChipsMatch(groups))
            return;
        CollapseSourceFold();
        ClearSourceChipRow();
        VisualElement? sourceLine = null;
        for (var i = 0; i < groups.Count; i++)
        {
            if (i % Sizes.SourceChipsPerRow == 0)
            {
                sourceLine = CreateSourceChipLine();
                if (i > 0)
                    sourceLine.style.marginTop = UiSpacing.Xs;
                _sourceChipRow.Add(sourceLine);
            }

            var group = groups[i];
            var face = group.Face(_selectedSourceKey, _encounteredSourceKeys);
            var chip = CreateSourceChip(face, () => OnSourceCellClicked(group), out var icon);
            chip.tooltip = string.Join("\n", group.Members.Select(SourceTooltip));
            if (group.IsFolded)
                chip.Add(CreateSourceCountBadge(group.Members.Count));
            chip.style.marginRight =
                i % Sizes.SourceChipsPerRow == Sizes.SourceChipsPerRow - 1 ? 0f : UiSpacing.Sm;
            var leadKey = group.Lead.SourceKey;
            _sourceChips[leadKey] = chip;
            _sourceChipIcons[leadKey] = icon;
            _sourceFaces[leadKey] = face.SourceKey;
            _sourceGroups.Add(group);
            sourceLine!.Add(chip);
        }
    }

    private bool SourceChipsMatch(IReadOnlyList<CollectionSourceGroup> groups)
    {
        if (_sourceGroups.Count != groups.Count)
            return false;
        for (var i = 0; i < groups.Count; i++)
            if (!_sourceGroups[i].HasSameMembers(groups[i]))
                return false;
        return true;
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
        _sourceFaces.Clear();
        _sourceGroups.Clear();
        _sourceChipRow?.Clear();
    }

    // A folded cell shows the member that matters, is selected when a member is, and glows when
    // the run met a member and none is selected.
    private void RefreshSourceCells()
    {
        foreach (var group in _sourceGroups)
        {
            var leadKey = group.Lead.SourceKey;
            if (!_sourceChips.TryGetValue(leadKey, out var chip))
                continue;
            var selected = group.IsSelected(_selectedSourceKey);
            var face = group.Face(_selectedSourceKey, _encounteredSourceKeys);
            _sourceChipIcons.TryGetValue(leadKey, out var icon);
            if (
                icon != null
                && (
                    !_sourceFaces.TryGetValue(leadKey, out var shown)
                    || !string.Equals(shown, face.SourceKey, StringComparison.Ordinal)
                )
            )
            {
                ShowSourceFace(icon, face);
                _sourceFaces[leadKey] = face.SourceKey;
            }
            RefreshSourceChip(chip, selected);
            RefreshSourceSelectionRing(chip, selected, _foldExpansion.IsOpen(group));
            if (icon != null)
                RefreshSourceEncounterHighlight(
                    icon,
                    group.IsEncountered(_encounteredSourceKeys) && !selected
                );
            chip.SetEnabled(_sourceSelectorEnabled);
            chip.style.opacity = _sourceSelectorEnabled ? 1f : 0.58f;
        }
        SyncSourceFold();
    }

    private void OnSourceCellClicked(CollectionSourceGroup group)
    {
        var key = _foldExpansion.ClickCell(group);
        RefreshSourceCells();
        if (key != null)
            _commands.ToggleSource(key);
    }

    private void OnFoldMemberClicked(CollectionSourceOptionViewModel member)
    {
        var key = _foldExpansion.ClickMember(member);
        RefreshSourceCells();
        _commands.ToggleSource(key);
    }

    internal bool IsSourceFoldOpen => _foldExpansion.Open != null;

    // Escape, the card inspection, a hidden panel or a rebuilt row closes the open fold; the
    // selection stays.
    internal void CollapseSourceFold()
    {
        if (_foldExpansion.Collapse())
            RefreshSourceCells();
    }

    // A press released outside the open fold's cell and strip closes it. Released, not pressed:
    // the cells under the strip move up when it closes, and the release must still reach the
    // cell it started on.
    private void OnRootPointerUp(PointerUpEvent evt)
    {
        if (_foldExpansion.Open is not { } open)
            return;
        if (evt.target is VisualElement target)
        {
            if (_foldStrip != null && IsWithin(target, _foldStrip))
                return;
            if (
                _sourceChips.TryGetValue(open.Lead.SourceKey, out var cell)
                && IsWithin(target, cell)
            )
                return;
        }
        CollapseSourceFold();
    }

    private static bool IsWithin(VisualElement element, VisualElement container)
    {
        for (var current = element; current != null; current = current.parent)
            if (current == container)
                return true;
        return false;
    }

    // Draws the open fold's member strip under the line that holds its cell, or removes it.
    private void SyncSourceFold()
    {
        _foldStrip?.RemoveFromHierarchy();
        _foldStrip = null;
        _foldMemberChips.Clear();
        if (_sourceChipRow == null || _foldExpansion.Open is not { } open)
            return;
        var index = _sourceGroups.IndexOf(open);
        if (index < 0)
        {
            _foldExpansion.Collapse();
            return;
        }

        var strip = new VisualElement();
        strip.style.flexDirection = FlexDirection.Row;
        strip.style.flexWrap = Wrap.Wrap;
        strip.style.alignSelf = Align.Stretch;
        strip.style.marginTop = UiSpacing.Xs;
        UiStyle.Padding(strip.style, UiSpacing.Xs);
        // The strip reaches out by its inset, so its members stand on the cells' columns.
        strip.style.marginLeft = strip.style.marginRight = -(UiSpacing.Xs + Borders.Thin);
        strip.style.backgroundColor = InGameTheme.NeutralGround;
        UiStyle.Border(strip.style, Borders.Thin, ChipBorder);
        UiStyle.Radius(strip.style, Radii.CollectionPortraitChip);
        for (var i = 0; i < open.Members.Count; i++)
        {
            var member = open.Members[i];
            var chip = CreateSourceChip(member, () => OnFoldMemberClicked(member), out var icon);
            chip.tooltip = SourceTooltip(member);
            chip.style.marginRight =
                i % Sizes.SourceChipsPerRow == Sizes.SourceChipsPerRow - 1 ? 0f : UiSpacing.Sm;
            var selected = string.Equals(
                member.SourceKey,
                _selectedSourceKey,
                StringComparison.Ordinal
            );
            RefreshSourceChip(chip, selected);
            RefreshSourceSelectionRing(chip, selected);
            RefreshSourceEncounterHighlight(
                icon,
                _encounteredSourceKeys.Contains(member.SourceKey) && !selected
            );
            _foldMemberChips[member.SourceKey] = (chip, icon);
            strip.Add(chip);
        }
        _foldStrip = strip;
        _sourceChipRow.Insert(
            Mathf.Min(index / Sizes.SourceChipsPerRow + 1, _sourceChipRow.childCount),
            strip
        );
    }

    private Button CreateSourceChip(
        CollectionSourceOptionViewModel face,
        Action onClick,
        out VisualElement icon
    )
    {
        var chip = CreateButton(
            string.Empty,
            onClick,
            CurrentSourceChipBox(),
            CurrentSourceChipBox(),
            fixedWidth: false
        );
        chip.style.flexDirection = FlexDirection.Row;
        chip.style.justifyContent = Justify.Center;
        chip.style.alignItems = Align.Center;
        chip.style.marginRight = UiSpacing.Sm;
        chip.style.marginBottom = UiSpacing.Xs;
        StyleCollectionChip(chip, InGameTheme.NeutralChip, InGameTheme.NeutralInk);
        UiStyle.Radius(chip.style, Radii.CollectionPortraitChip);

        icon = CreateSourceChipIcon(face.DisplayName);
        chip.Add(icon);
        chip.Add(CreateSourceSelectionRing());
        ResizeSourceChip(chip, icon, CurrentSourceChipBox());
        LoadSourceChipIcon(face.SourceKey, face.RepresentativeTemplateId, icon);
        return chip;
    }

    private static void ShowSourceFace(VisualElement icon, CollectionSourceOptionViewModel face)
    {
        var initials = icon.Q<Label>(SourceChipInitialsName);
        if (initials != null)
            initials.text = GetInitials(face.DisplayName);
        LoadSourceChipIcon(face.SourceKey, face.RepresentativeTemplateId, icon);
    }

    private static string SourceTooltip(CollectionSourceOptionViewModel source) =>
        string.IsNullOrWhiteSpace(source.Description)
            ? source.DisplayName
            : $"{source.DisplayName} - {source.Description}";

    // How many sources share a folded cell, in its lower right corner.
    private static Label CreateSourceCountBadge(int count)
    {
        var badge = CreateLabel(Sizes.FontCorner, FontStyle.Bold, InGameTheme.NeutralInk);
        badge.name = SourceChipCountName;
        badge.text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        badge.pickingMode = PickingMode.Ignore;
        badge.style.position = Position.Absolute;
        badge.style.right = UiSpacing.Xxs;
        badge.style.bottom = UiSpacing.Xxs;
        badge.style.minWidth = Sizes.SourceCountBadgeSize;
        badge.style.height = Sizes.SourceCountBadgeSize;
        UiStyle.HorizontalPadding(badge.style, UiSpacing.Xxs);
        badge.style.unityTextAlign = TextAnchor.MiddleCenter;
        badge.style.backgroundColor = InGameTheme.WithAlpha(InGameTheme.NeutralGround, .88f);
        UiStyle.Border(badge.style, Borders.Thin, ChipBorder);
        UiStyle.Radius(badge.style, Sizes.SourceCountBadgeSize / 2f);
        return badge;
    }
}
