using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Ui;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTheBazaar.UI.Components;
using Il2CppTheBazaar.UIScripts;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Settings;

/// <summary>
/// The BazaarPlusPlus section of the game's own settings dialog on Android, the desktop's
/// <c>BppNativeSettingsSectionController</c> on IL2CPP: before the dialog's scroll spy wakes, the
/// Support section and its navigation toggle are cloned into a "BazaarPlusPlus" section and entry,
/// and every row of <see cref="BppSettingsDockCatalog.Definitions"/> is a clone of a native row
/// (a toggle, a dropdown, or a button) driven by its definition. The rows are the ones the host
/// registered through Core's <see cref="SettingsDockEntryRegistry"/>, the desktop's model.
/// On the phone the toggles stay one per line, where the desktop pairs them, so a label keeps the
/// content column's width. The rows keep the native look and size, and each is a
/// <see cref="TouchMetrics.MinTargetDp"/> touch target: the section's spacing grows until a row and
/// its share of the gaps reach that height, and an invisible hit area inside the row's control
/// covers the row and that share, so a tap anywhere on it reaches the control and no two rows'
/// areas overlap. The geometry is applied again whenever the dialog opens or the screen changes.
/// A plain class, not a component: the host ticks it (<see cref="Tick"/>) and the dialog's
/// <c>OnEnable</c> refreshes it.
/// </summary>
internal sealed class AndroidNativeSettingsSection
{
    private const string SectionObjectName = "BPP_SettingsSection";
    private const string NavigationObjectName = "BPP_SettingsNavigationToggle";
    private const string RowObjectPrefix = "BPP_SettingsRow_";
    private const string HitAreaObjectName = "BPP_HitArea";
    private const float InactiveAlpha = 0.5f;
    private const float SplitContainerExtraHeight = 24f;
    private const float RefreshInterval = 0.5f;

    // The dialog lays itself out over a few frames after it opens or the screen changes, so the
    // visibility measure waits this long after the last change.
    private const float VisibilitySettleSeconds = 1f;

    private static readonly List<AndroidNativeSettingsSection> Sections = new();
    private static long _installs;
    private static long _installFailures;
    private static long _rowWrites;
    private static long _refreshes;

    private readonly List<RowView> _rows = new();
    private readonly OptionsDialogController _dialog;
    private readonly Button? _closeButton;
    private readonly RectTransform _sectionRoot;
    private int _lastPresentationHash;
    private float _nextRefreshAt;
    private bool _wasVisible;
    private string _screenKey = string.Empty;
    private readonly ScrollRect? _scrollRect;
    private float _scrollPosition = float.NaN;
    private float _scrollMovedAt;
    private float _loggedScrollPosition = float.NaN;
    private float? _nativeSpacing;
    private readonly RectTransform _navigationRoot;
    private float _lastMinHitPx;
    private float _lastMinOptionPx;
    private int _lastHitOverlaps;
    private string? _pendingVisibility;
    private float _visibilityDueAt;

    private AndroidNativeSettingsSection(
        OptionsDialogController dialog,
        Button? closeButton,
        RectTransform sectionRoot,
        RectTransform navigationRoot,
        ScrollRect? scrollRect
    )
    {
        _scrollRect = scrollRect;
        _dialog = dialog;
        _closeButton = closeButton;
        _sectionRoot = sectionRoot;
        _navigationRoot = navigationRoot;
    }

    /// <summary>Whether a BazaarPlusPlus section is on screen, which the touch button stays out of.</summary>
    public static bool AnyVisible
    {
        get
        {
            foreach (var section in Sections)
            {
                if (section._wasVisible)
                    return true;
            }
            return false;
        }
    }

    public static BppLogField[] Summary() =>
        [
            ("settings_sections", Sections.Count),
            ("settings_installs", _installs),
            ("settings_install_failures", _installFailures),
            ("settings_row_writes", _rowWrites),
            ("settings_refreshes", _refreshes),
        ];

    /// <summary>
    /// The scroll spy of a settings dialog is about to wake: add the BazaarPlusPlus section and its
    /// navigation entry, so the spy wires them like its own.
    /// </summary>
    public static void TryInstall(ScrollSpyController scrollSpy)
    {
        var dialog = scrollSpy.GetComponentInParent<OptionsDialogController>(true);
        if (dialog == null || Find(dialog) != null)
            return;
        var definitions = BppSettingsDockCatalog.Definitions;
        if (definitions.Count == 0)
            return;

        RectTransform? stagedSection = null;
        RectTransform? stagedNavigation = null;
        Il2CppReferenceArray<ScrollSpyEntry>? originalEntries = null;
        var footer = new FooterRollback();
        var stage = "read_dialog";
        try
        {
            var entries = scrollSpy._entries;
            var content = scrollSpy._scrollRect?.content;
            var contactButton = dialog.ContactSupportButton;
            var toggleDonor = FirstActive(
                dialog._keywordLegend,
                dialog._socketEffectLegend,
                dialog._fastForwardFirstFight,
                dialog._hideOpponentNamesToggle,
                dialog._anonymousModeToggle
            );
            var choiceDonor = FirstActive(dialog.ResolutionDropdown, dialog._languageSetting);
            if (
                entries == null
                || entries.Length == 0
                || content == null
                || contactButton == null
                || toggleDonor == null
                || choiceDonor == null
            )
            {
                ReportInstallFailure(
                    stage,
                    $"entries={entries?.Length ?? -1} content={content != null} contact={contactButton != null} toggle={toggleDonor != null} choice={choiceDonor != null}"
                );
                return;
            }
            originalEntries = entries;

            stage = "support_section";
            var supportSection = FindDirectChildAncestor(content, contactButton.transform);
            var supportIndex =
                supportSection == null ? -1 : FindEntryIndex(entries, supportSection);
            if (supportSection == null || supportIndex < 0)
            {
                ReportInstallFailure(stage, "support_section_unavailable");
                return;
            }
            var supportNavigation = entries[supportIndex].NavButtonRoot;
            if (supportNavigation == null)
            {
                ReportInstallFailure(stage, "support_navigation_unavailable");
                return;
            }

            stage = "clone";
            stagedSection = CloneInactive(supportSection, supportSection.parent, SectionObjectName);
            stagedNavigation = CloneInactive(
                supportNavigation,
                supportNavigation.parent,
                NavigationObjectName
            );
            ConfigureNavigation(stagedNavigation);

            stage = "rows";
            var section = new AndroidNativeSettingsSection(
                dialog,
                dialog.CloseButton,
                stagedSection,
                stagedNavigation,
                scrollSpy._scrollRect
            );
            section.BuildSection(content, contactButton, toggleDonor, choiceDonor, definitions);

            stage = "footer";
            HideNativeFooter(dialog, supportSection, footer);

            stage = "entries";
            var expanded = new Il2CppReferenceArray<ScrollSpyEntry>((long)entries.Length + 1L);
            var target = 0;
            for (var index = 0; index < entries.Length; index++)
            {
                expanded[target++] = entries[index];
                if (index != supportIndex)
                    continue;
                var entry = new ScrollSpyEntry
                {
                    NavButtonRoot = stagedNavigation,
                    SectionRoot = stagedSection,
                };
                expanded[target++] = entry;
            }
            stagedSection.SetSiblingIndex(supportSection.GetSiblingIndex() + 1);
            stagedNavigation.SetSiblingIndex(supportNavigation.GetSiblingIndex() + 1);
            scrollSpy._entries = expanded;
            stagedSection.gameObject.SetActive(true);
            stagedNavigation.gameObject.SetActive(true);
            section.RebuildLayout();
            section.Refresh(force: true);
            section.ApplyTouchTargets("installed");
            Sections.Add(section);
            _installs++;
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.settings.section_installed"),
                ("rows", section._rows.Count),
                ("entries", expanded.Length),
                ("toggle_donor", toggleDonor.name),
                ("choice_donor", choiceDonor.name),
                ("footer_hidden", footer.Hidden.Count)
            );
            stagedSection = null;
            stagedNavigation = null;
        }
        catch (Exception exception)
        {
            if (originalEntries != null && scrollSpy._entries?.Length != originalEntries.Length)
                scrollSpy._entries = originalEntries;
            // The footer comes back and the split container returns to its own frame.
            var footerRestored = footer.Undo();
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.settings.install_rolled_back"),
                ("stage", stage),
                ("footer_restored", footerRestored),
                ("split_container_restored", footer.SplitContainerRestored)
            );
            if (stagedSection != null)
                UnityEngine.Object.DestroyImmediate(stagedSection.gameObject);
            if (stagedNavigation != null)
                UnityEngine.Object.DestroyImmediate(stagedNavigation.gameObject);
            ReportInstallFailure(stage, "exception", exception);
        }
    }

    /// <summary>
    /// <paramref name="dialog"/> was enabled: its section rereads the settings. Its rows are
    /// laid out by the section's next <see cref="Tick"/>, at or after this call, which sees it
    /// opened; that was the later of the two passes an open used to run.
    /// </summary>
    public static void Opened(OptionsDialogController dialog)
    {
        var section = Find(dialog);
        if (section == null || section._sectionRoot == null)
            return;
        section.Refresh(force: true);
        // Closed and opened again between two ticks still reads as opened.
        section._wasVisible = false;
    }

    /// <summary>
    /// A settings dialog changed language: redraw every section. The section stays open, so no
    /// tick sees it opened, and it lays out here.
    /// </summary>
    public static void RefreshAll()
    {
        for (var index = Sections.Count - 1; index >= 0; index--)
        {
            var section = Sections[index];
            try
            {
                // A dialog destroyed since the last tick (a scene change) is dropped, as in Tick.
                if (DropIfDestroyed(index, "refresh"))
                    continue;
                section.RebuildLayout();
                section.Refresh(force: true);
                if (section._sectionRoot.gameObject.activeInHierarchy)
                    section.ApplyTouchTargets("refreshed");
            }
            catch (Exception exception)
            {
                Sections.RemoveAt(index);
                AndroidPatchHost.ReportProbeError("settings_section", exception);
            }
        }
    }

    // Unity's null: the dialog or the section was destroyed with its scene.
    private static bool DropIfDestroyed(int index, string during)
    {
        var section = Sections[index];
        if (section._dialog != null && section._sectionRoot != null)
            return false;
        Sections.RemoveAt(index);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.settings.section_dropped"),
            ("during", during),
            ("sections", Sections.Count)
        );
        return true;
    }

    /// <summary>Main thread, every frame: drops dead dialogs and keeps a visible section current.</summary>
    public static void Tick(float now)
    {
        for (var index = Sections.Count - 1; index >= 0; index--)
        {
            var section = Sections[index];
            try
            {
                if (DropIfDestroyed(index, "tick"))
                    continue;
                var visible = section._sectionRoot.gameObject.activeInHierarchy;
                var screenKey = visible ? AndroidScreenKey() : section._screenKey;
                if (visible && (!section._wasVisible || screenKey != section._screenKey))
                {
                    // Opened again, or the screen changed under it: lay the rows out anew.
                    var reason = section._wasVisible ? "screen_changed" : "opened";
                    section.RebuildLayout();
                    section.Refresh(force: true);
                    section.ApplyTouchTargets(reason);
                    section._pendingVisibility = reason;
                    section._visibilityDueAt = now + VisibilitySettleSeconds;
                }
                else if (!visible && section._wasVisible)
                {
                    section._pendingVisibility = null;
                    LogVisibility(section, "closed");
                }
                section._wasVisible = visible;
                if (
                    visible
                    && section._pendingVisibility != null
                    && now >= section._visibilityDueAt
                )
                {
                    LogVisibility(section, section._pendingVisibility);
                    section._pendingVisibility = null;
                }
                if (visible)
                    section.ObserveScroll(now);
                if (!visible || now < section._nextRefreshAt)
                    continue;
                section._nextRefreshAt = now + RefreshInterval;
                section.Refresh(force: false);
            }
            catch (Exception exception)
            {
                Sections.RemoveAt(index);
                AndroidPatchHost.ReportProbeError("settings_section", exception);
            }
        }
    }

    private static AndroidNativeSettingsSection? Find(OptionsDialogController dialog)
    {
        foreach (var section in Sections)
        {
            if (section._dialog != null && section._dialog.Pointer == dialog.Pointer)
                return section;
        }
        return null;
    }

    private void BuildSection(
        RectTransform content,
        Button actionDonor,
        Toggle toggleDonor,
        TMP_Dropdown choiceDonor,
        IReadOnlyList<BppSettingsDockDefinition> definitions
    )
    {
        StripNativeLocalization(_sectionRoot);
        var header = SetSectionHeader(_sectionRoot);
        RemoveDonorRows(_sectionRoot, header);

        foreach (
            var kind in new[]
            {
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Choice,
                BppSettingsControlKind.Action,
            }
        )
        {
            foreach (var definition in definitions)
            {
                if (definition.ControlKind != kind)
                    continue;
                Selectable donor = kind switch
                {
                    BppSettingsControlKind.Toggle => toggleDonor,
                    BppSettingsControlKind.Choice => choiceDonor,
                    _ => actionDonor,
                };
                var donorSection =
                    FindDirectChildAncestor(content, donor.transform)
                    ?? throw new InvalidOperationException(
                        $"No donor section for '{definition.Key}'."
                    );
                var donorRow =
                    FindSingleSelectableRow(donorSection, donor)
                    ?? throw new InvalidOperationException($"No donor row for '{definition.Key}'.");
                var rowObject = UnityEngine.Object.Instantiate(
                    donorRow.gameObject,
                    _sectionRoot,
                    false
                );
                rowObject.name = RowObjectPrefix + definition.Key;
                rowObject.SetActive(true);
                StripNativeLocalization(rowObject.transform);
                var rowRect =
                    rowObject.GetComponent<RectTransform>()
                    ?? throw new InvalidOperationException($"Row '{definition.Key}' has no rect.");
                _rows.Add(ConfigureRow(definition, rowRect));
            }
        }
    }

    private RowView ConfigureRow(BppSettingsDockDefinition definition, RectTransform rowRoot)
    {
        Toggle? toggle = null;
        TMP_Dropdown? choice = null;
        Button? action = null;
        switch (definition.ControlKind)
        {
            case BppSettingsControlKind.Toggle:
                toggle =
                    rowRoot.GetComponentInChildren<Toggle>(true)
                    ?? throw new InvalidOperationException($"No toggle in '{definition.Key}'.");
                toggle.onValueChanged = new Toggle.ToggleEvent();
                toggle.onValueChanged.AddListener(
                    (UnityAction<bool>)
                        new Action<bool>(enabled =>
                        {
                            Write(definition, "toggle", enabled);
                            definition.WriteToggle?.Invoke(enabled);
                            Refresh(force: true);
                        })
                );
                RemoveOtherSelectables(rowRoot, toggle);
                break;
            case BppSettingsControlKind.Choice:
                choice =
                    rowRoot.GetComponentInChildren<TMP_Dropdown>(true)
                    ?? throw new InvalidOperationException($"No dropdown in '{definition.Key}'.");
                choice.onValueChanged = new TMP_Dropdown.DropdownEvent();
                choice.onValueChanged.AddListener(
                    (UnityAction<int>)
                        new Action<int>(selected => SelectChoice(definition, selected))
                );
                RemoveOtherSelectables(rowRoot, choice);
                break;
            default:
                action =
                    rowRoot.GetComponentInChildren<Button>(true)
                    ?? throw new InvalidOperationException($"No button in '{definition.Key}'.");
                action.onClick = new Button.ButtonClickedEvent();
                action.onClick.AddListener((UnityAction)new Action(() => Activate(definition)));
                RemoveOtherSelectables(rowRoot, action);
                break;
        }
        return new RowView(definition, rowRoot, toggle, choice, action);
    }

    private void SelectChoice(BppSettingsDockDefinition definition, int selected)
    {
        var state = definition.ResolveChoiceState?.Invoke(CurrentLanguageCode);
        if (state == null || (state.HasSyntheticCurrentOption && selected == 0))
            return;
        var standard = selected - (state.HasSyntheticCurrentOption ? 1 : 0);
        Write(definition, "choice", standard);
        definition.SelectStandardChoice?.Invoke(standard);
        Refresh(force: true);
    }

    private void Activate(BppSettingsDockDefinition definition)
    {
        if (!definition.IsActive())
            return;
        Write(definition, "action", "activate");
        if (definition.CollapseAfterActivate)
            _closeButton?.onClick.Invoke();
        definition.Activate?.Invoke();
        Refresh(force: true);
    }

    private static void Write(BppSettingsDockDefinition definition, string control, object value)
    {
        _rowWrites++;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.settings.row_tapped"),
            ("key", definition.Key),
            ("control", control),
            ("value", value)
        );
    }

    private void Refresh(bool force)
    {
        if (_rows.Count == 0)
            return;
        var languageCode = CurrentLanguageCode;
        var hash = 17;
        foreach (var row in _rows)
        {
            var definition = row.Definition;
            hash = (hash * 31) ^ definition.Key.GetHashCode();
            hash = (hash * 31) ^ definition.IsActive().GetHashCode();
            hash = (hash * 31) ^ definition.ResolveLabel(languageCode).GetHashCode();
            switch (definition.ControlKind)
            {
                case BppSettingsControlKind.Toggle:
                    hash = (hash * 31) ^ (definition.ReadToggle?.Invoke() == true).GetHashCode();
                    hash =
                        (hash * 31) ^ (definition.IsInteractable?.Invoke() != false).GetHashCode();
                    break;
                case BppSettingsControlKind.Choice:
                {
                    var state = definition.ResolveChoiceState?.Invoke(languageCode);
                    if (state == null)
                        break;
                    hash = (hash * 31) ^ state.SelectedIndex;
                    foreach (var option in state.Options)
                        hash = (hash * 31) ^ option.GetHashCode();
                    break;
                }
            }
        }
        if (!force && hash == _lastPresentationHash)
            return;
        _lastPresentationHash = hash;
        _refreshes++;
        foreach (var row in _rows)
            ApplyRow(row, languageCode);
    }

    private static void ApplyRow(RowView row, string languageCode)
    {
        SetRowLabel(row, row.Definition.ResolveLabel(languageCode));
        switch (row.Definition.ControlKind)
        {
            case BppSettingsControlKind.Toggle when row.Toggle != null:
                row.Toggle.SetIsOnWithoutNotify(row.Definition.ReadToggle?.Invoke() == true);
                row.Toggle.interactable = row.Definition.IsInteractable?.Invoke() != false;
                break;
            case BppSettingsControlKind.Choice when row.Choice != null:
            {
                var state = row.Definition.ResolveChoiceState?.Invoke(languageCode);
                if (state == null)
                    break;
                row.Choice.options.Clear();
                foreach (var option in state.Options)
                    row.Choice.options.Add(new TMP_Dropdown.OptionData(option));
                row.Choice.SetValueWithoutNotify(
                    Math.Clamp(state.SelectedIndex, 0, Math.Max(0, row.Choice.options.Count - 1))
                );
                row.Choice.RefreshShownValue();
                row.Choice.interactable = true;
                break;
            }
            case BppSettingsControlKind.Action when row.Action != null:
            {
                var active = row.Definition.IsActive();
                row.Action.interactable = active;
                // The donor button keeps its colors when not interactable; dim it instead.
                var group =
                    row.Root.GetComponent<CanvasGroup>()
                    ?? row.Root.gameObject.AddComponent<CanvasGroup>();
                group.alpha = active ? 1f : InactiveAlpha;
                break;
            }
        }
    }

    // Every row becomes at least a 48dp target without changing its native size: the section's
    // spacing grows until the shortest row and one gap reach the target in screen pixels, and each
    // control gets a transparent hit area over its row and half a gap on either side, so
    // neighbouring rows' areas meet and never overlap.
    private void ApplyTouchTargets(string reason)
    {
        if (_rows.Count == 0)
            return;
        _screenKey = AndroidScreenKey();
        var canvas = _sectionRoot.GetComponentInParent<Canvas>(true)?.rootCanvas;
        var scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        var group = _sectionRoot.GetComponent<VerticalLayoutGroup>();
        var spacingBefore = group?.spacing ?? 0f;
        if (group != null)
        {
            _nativeSpacing ??= group.spacing;
            var shortest = float.MaxValue;
            foreach (var row in _rows)
                shortest = Math.Min(shortest, row.Root.rect.height);
            var missing = TouchMetrics.MissingTargetPixels(
                (shortest + _nativeSpacing.Value) * scale,
                Screen.dpi
            );
            var spacing = _nativeSpacing.Value + missing / scale;
            if (Math.Abs(spacing - group.spacing) > 0.5f)
            {
                group.spacing = spacing;
                RebuildLayout();
            }
        }
        var gap = group?.spacing ?? 0f;

        var minHitPx = float.MaxValue;
        var minOptionPx = float.MaxValue;
        var overlaps = 0;
        RectTransform? previous = null;
        foreach (var row in _rows)
        {
            if (row.Choice != null)
                minOptionPx = Math.Min(minOptionPx, FitOptions(row, scale));
            var control = (row.Toggle as Selectable) ?? (row.Choice as Selectable) ?? row.Action;
            var hit = control == null ? null : EnsureHitArea(control, row.Root, gap);
            if (hit != null)
            {
                minHitPx = Math.Min(minHitPx, WorldHeight(hit) / WorldUnitsPerPixel(canvas));
                if (previous != null && Overlaps(previous, hit))
                    overlaps++;
                previous = hit;
            }
        }
        _lastMinHitPx = minHitPx == float.MaxValue ? 0f : minHitPx;
        _lastMinOptionPx = minOptionPx == float.MaxValue ? 0f : minOptionPx;
        _lastHitOverlaps = overlaps;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.settings.touch_targets"),
            ("reason", reason),
            ("screen", _screenKey),
            ("canvas_scale", Math.Round(scale, 3)),
            ("rows", _rows.Count),
            ("layout_group", group != null),
            ("spacing_before", Math.Round(spacingBefore, 1)),
            ("spacing", Math.Round(gap, 1)),
            ("min_hit_px", Math.Round(minHitPx == float.MaxValue ? 0f : minHitPx)),
            ("min_option_px", Math.Round(minOptionPx == float.MaxValue ? 0f : minOptionPx)),
            ("target_px", Math.Round(TouchMetrics.MissingTargetPixels(0f, Screen.dpi))),
            ("hit_overlaps", overlaps)
        );
    }

    // The dropdown's open list draws one copy of its template item per option, at the item's
    // height, keeping the item's top and bottom padding in the list's content. Every fit starts
    // again from the clone's native template: the item grows to a 48dp target by what it lacks on
    // this screen, the content by as much, and the list to hold every option but never below its
    // native height. So a screen where the native item is already a full target, or fewer options,
    // shrink it back. The game's own dropdowns keep their template.
    private static float FitOptions(RowView row, float scale)
    {
        var choice = row.Choice;
        var template = choice?.template;
        var item = template
            ?.GetComponentInChildren<Toggle>(true)
            ?.transform.TryCast<RectTransform>();
        var content = item?.parent?.TryCast<RectTransform>();
        var viewport = template?.GetComponent<ScrollRect>()?.viewport;
        if (choice == null || template == null || item == null || content == null)
            return 0f;
        var native = row.NativeOptions ??= new OptionsGeometry(
            template.sizeDelta,
            content.sizeDelta,
            content.anchoredPosition,
            item.sizeDelta,
            item.anchoredPosition
        );
        template.sizeDelta = native.TemplateSize;
        content.sizeDelta = native.ContentSize;
        content.anchoredPosition = native.ContentPosition;
        item.sizeDelta = native.ItemSize;
        item.anchoredPosition = native.ItemPosition;
        var height = item.rect.height;
        var missing = TouchMetrics.MissingTargetPixels(height * scale, Screen.dpi) / scale;
        if (missing > 0.5f)
        {
            // Both keep their top edge: the content grows downwards, and the item, anchored in it,
            // grows by as much, so the padding above and below the item stays what it was.
            content.sizeDelta = new Vector2(content.sizeDelta.x, content.sizeDelta.y + missing);
            content.anchoredPosition = new Vector2(
                content.anchoredPosition.x,
                content.anchoredPosition.y - missing * (1f - content.pivot.y)
            );
            item.sizeDelta = new Vector2(item.sizeDelta.x, item.sizeDelta.y + missing);
            item.anchoredPosition = new Vector2(
                item.anchoredPosition.x,
                item.anchoredPosition.y + missing * (item.pivot.y - item.anchorMin.y)
            );
            height += missing;
        }
        // The list shrinks itself to its items when it opens, but never grows past the template.
        var padding = content.rect.height - height;
        var frame = template.rect.height - (viewport?.rect.height ?? template.rect.height);
        var needed = Math.Max(1, choice.options.Count) * height + padding + frame;
        if (needed > template.rect.height + 0.5f)
            template.sizeDelta = new Vector2(
                template.sizeDelta.x,
                template.sizeDelta.y + needed - template.rect.height
            );
        return height * scale;
    }

    // World units per screen pixel: one on an overlay canvas, the canvas's own on a camera one.
    private static float WorldUnitsPerPixel(Canvas? canvas)
    {
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return 1f;
        var root = canvas.transform.TryCast<RectTransform>();
        if (root == null || Screen.height <= 0)
            return 1f;
        return WorldHeight(root) / Screen.height;
    }

    // A transparent child of the control over its row and half a gap above and below: a tap on it
    // is a tap on the control.
    private static RectTransform EnsureHitArea(Selectable control, RectTransform rowRoot, float gap)
    {
        var existing = control.transform.Find(HitAreaObjectName)?.TryCast<RectTransform>();
        RectTransform hit;
        if (existing != null)
            hit = existing;
        else
        {
            var area = new GameObject(HitAreaObjectName);
            hit = area.AddComponent<RectTransform>();
            hit.SetParent(control.transform, false);
            hit.SetAsFirstSibling();
            var image = area.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
        }
        hit.anchorMin = new Vector2(0.5f, 0.5f);
        hit.anchorMax = new Vector2(0.5f, 0.5f);
        hit.pivot = new Vector2(0.5f, 0.5f);
        var rowRect = rowRoot.rect;
        var lossy = control.transform.lossyScale;
        var rowLossy = rowRoot.lossyScale;
        hit.sizeDelta = new Vector2(
            rowRect.width * SafeRatio(rowLossy.x, lossy.x),
            (rowRect.height + gap) * SafeRatio(rowLossy.y, lossy.y)
        );
        hit.position = rowRoot.TransformPoint(rowRect.center);
        return hit;
    }

    private static float SafeRatio(float numerator, float denominator) =>
        Math.Abs(denominator) < 1e-6f ? 1f : numerator / denominator;

    private static bool Overlaps(RectTransform above, RectTransform below)
    {
        var a = WorldVerticalSpan(above);
        var b = WorldVerticalSpan(below);
        var tolerance = Math.Min(a.Max - a.Min, b.Max - b.Min) * 0.01f;
        return a.Min < b.Max - tolerance && b.Min < a.Max - tolerance;
    }

    private static float WorldHeight(RectTransform rect)
    {
        var span = WorldVerticalSpan(rect);
        return span.Max - span.Min;
    }

    private static (float Min, float Max) WorldVerticalSpan(RectTransform rect)
    {
        var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4L);
        rect.GetWorldCorners(corners);
        var min = Math.Min(corners[0].y, corners[1].y);
        var max = Math.Max(corners[0].y, corners[1].y);
        return (min, max);
    }

    private static string AndroidScreenKey() => TouchScreen.Key();

    private void RebuildLayout()
    {
        for (
            var current = _sectionRoot;
            current != null;
            current = current.parent?.TryCast<RectTransform>()
        )
            LayoutRebuilder.ForceRebuildLayoutImmediate(current);
    }

    // The desktop hides the dialog's "changes are saved automatically" footer and grows the split
    // container into the space, so the added navigation entry fits the column.
    private static void HideNativeFooter(
        OptionsDialogController dialog,
        RectTransform supportSection,
        FooterRollback rollback
    )
    {
        var splitContainer = SplitContainer(dialog);
        var scalerOffset = splitContainer?.parent;
        var footerLabel = scalerOffset?.Find("Notice_ChangeAuto")?.gameObject;
        var divider = scalerOffset?.Find("Divider_1")?.gameObject;
        Hide(footerLabel, rollback.Hidden);
        Hide(divider, rollback.Hidden);
        Hide(supportSection.Find("Buffer")?.gameObject, rollback.Hidden);
        var grown =
            splitContainer != null
            && ExpandSplitContainer(splitContainer, footerLabel, divider, rollback);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.settings.footer_layout"),
            ("split_container", splitContainer != null),
            ("footer_label", footerLabel != null),
            ("divider", divider != null),
            ("hidden", rollback.Hidden.Count),
            ("grown", grown)
        );
        var dialogRect = dialog.transform.TryCast<RectTransform>();
        if (dialogRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(dialogRect);
    }

    private static RectTransform? SplitContainer(OptionsDialogController dialog) =>
        dialog.transform.Find("ScalerOffset/SplitContainer")?.TryCast<RectTransform>();

    private static bool ExpandSplitContainer(
        RectTransform splitContainer,
        GameObject? footerLabel,
        GameObject? divider,
        FooterRollback rollback
    )
    {
        var parent = splitContainer.parent?.TryCast<RectTransform>();
        if (parent == null)
            return false;
        // The rects' own corners, in the parent's space: the footer is hidden by now, and the
        // container's children (the scrolled content) reach far below its frame.
        var targetBottom = float.PositiveInfinity;
        foreach (var footer in new[] { footerLabel, divider })
        {
            var footerRect = footer?.transform.TryCast<RectTransform>();
            if (footerRect == null)
                continue;
            targetBottom = Math.Min(targetBottom, LocalBottom(parent, footerRect));
        }
        if (!float.IsFinite(targetBottom))
            return false;
        var currentBottom = LocalBottom(parent, splitContainer);
        var released = currentBottom - targetBottom;
        if (released <= 0f)
            return false;
        var half = (released + SplitContainerExtraHeight) * 0.5f;
        rollback.RecordSplitContainer(splitContainer);
        splitContainer.offsetMin = new Vector2(
            splitContainer.offsetMin.x,
            splitContainer.offsetMin.y - half
        );
        splitContainer.offsetMax = new Vector2(
            splitContainer.offsetMax.x,
            splitContainer.offsetMax.y + half
        );
        splitContainer.anchoredPosition = new Vector2(splitContainer.anchoredPosition.x, 0f);
        return true;
    }

    private static float LocalBottom(RectTransform space, RectTransform rect)
    {
        var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4L);
        rect.GetWorldCorners(corners);
        return Math.Min(
            space.InverseTransformPoint(corners[0]).y,
            space.InverseTransformPoint(corners[3]).y
        );
    }

    private static void Hide(GameObject? target, List<(GameObject Target, bool WasActive)> hidden)
    {
        if (target == null)
            return;
        hidden.Add((target, target.activeSelf));
        target.SetActive(false);
    }

    private static void ConfigureNavigation(RectTransform navigationRoot)
    {
        StripNativeLocalization(navigationRoot);
        var toggle =
            navigationRoot.GetComponent<Toggle>()
            ?? throw new InvalidOperationException("The Support navigation entry is not a Toggle.");
        toggle.onValueChanged = new Toggle.ToggleEvent();
        toggle.SetIsOnWithoutNotify(false);
        foreach (var label in navigationRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            label.fontStyle &= ~FontStyles.UpperCase;
            label.text = "BazaarPlusPlus";
        }
    }

    private static TextMeshProUGUI? SetSectionHeader(RectTransform sectionRoot)
    {
        var header = LargestLabel(sectionRoot, insideSelectable: false);
        if (header != null)
            header.text = "BazaarPlusPlus";
        return header;
    }

    private static TextMeshProUGUI? LargestLabel(Transform root, bool insideSelectable)
    {
        TextMeshProUGUI? best = null;
        foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (!insideSelectable && label.GetComponentInParent<Selectable>(true) != null)
                continue;
            if (best == null || label.fontSize > best.fontSize)
                best = label;
        }
        return best;
    }

    private static void SetRowLabel(RowView row, string text)
    {
        TextMeshProUGUI? best = null;
        foreach (var label in row.Root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (row.Choice != null && label.transform.IsChildOf(row.Choice.transform))
                continue;
            if (best == null || label.fontSize > best.fontSize)
                best = label;
        }
        if (best != null)
            best.text = text;
    }

    // Everything the Support clone carried but its header goes: the rows are added afterwards.
    private static void RemoveDonorRows(RectTransform sectionRoot, TextMeshProUGUI? header)
    {
        var headerRoot =
            header == null ? null : FindDirectChildAncestor(sectionRoot, header.transform);
        var remove = new List<GameObject>();
        for (var index = 0; index < sectionRoot.childCount; index++)
        {
            var child = sectionRoot.GetChild(index);
            if (headerRoot == null || child.Pointer != headerRoot.Pointer)
                remove.Add(child.gameObject);
        }
        foreach (var child in remove)
            UnityEngine.Object.DestroyImmediate(child);
    }

    private static void RemoveOtherSelectables(RectTransform rowRoot, Selectable keep)
    {
        foreach (var selectable in rowRoot.GetComponentsInChildren<Selectable>(true))
        {
            if (
                selectable.Pointer != keep.Pointer
                && !selectable.transform.IsChildOf(keep.transform)
            )
                UnityEngine.Object.DestroyImmediate(selectable);
        }
    }

    private static void StripNativeLocalization(Transform root)
    {
        foreach (var localizable in root.GetComponentsInChildren<LocalizableTextComponent>(true))
            UnityEngine.Object.DestroyImmediate(localizable);
    }

    private static RectTransform CloneInactive(RectTransform source, Transform parent, string name)
    {
        var clone = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
        clone.name = name;
        clone.SetActive(false);
        return clone.GetComponent<RectTransform>()
            ?? throw new InvalidOperationException($"Clone '{name}' has no rect.");
    }

    private static RectTransform? FindDirectChildAncestor(Transform parent, Transform descendant)
    {
        for (var current = descendant; current != null; current = current.parent)
        {
            var currentParent = current.parent;
            if (currentParent == null || current.Pointer == parent.Pointer)
                return null;
            if (currentParent.Pointer == parent.Pointer)
                return current.TryCast<RectTransform>();
        }
        return null;
    }

    private static RectTransform? FindSingleSelectableRow(
        RectTransform sectionRoot,
        Selectable donor
    )
    {
        var current = donor.transform.TryCast<RectTransform>();
        if (current == null)
            return null;
        var best = current;
        while (current.parent != null && current.parent.Pointer != sectionRoot.Pointer)
        {
            var parent = current.parent.TryCast<RectTransform>();
            if (parent == null || ContainsSelectableOutsideDonor(parent, donor))
                break;
            best = parent;
            current = parent;
        }
        return best;
    }

    private static bool ContainsSelectableOutsideDonor(Transform root, Selectable donor)
    {
        foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
        {
            if (
                selectable.Pointer == donor.Pointer
                || selectable.transform.IsChildOf(donor.transform)
            )
                continue;
            return true;
        }
        return false;
    }

    private static int FindEntryIndex(
        Il2CppReferenceArray<ScrollSpyEntry> entries,
        RectTransform section
    )
    {
        for (var index = 0; index < entries.Length; index++)
        {
            var root = entries[index]?.SectionRoot;
            if (root != null && root.Pointer == section.Pointer)
                return index;
        }
        return -1;
    }

    private static T? FirstActive<T>(params T?[] candidates)
        where T : Component
    {
        foreach (var candidate in candidates)
        {
            if (candidate != null && candidate.gameObject.activeInHierarchy)
                return candidate;
        }
        foreach (var candidate in candidates)
        {
            if (candidate != null)
                return candidate;
        }
        return null;
    }

    private static void ReportInstallFailure(
        string stage,
        string reason,
        Exception? exception = null
    )
    {
        _installFailures++;
        var logEvent = new BppLogEvent(
            BppLogFeatureScope.Android,
            "android.settings.section_install_failed",
            storm: ["stage"]
        );
        if (exception == null)
            BppLog.WarnEvent(logEvent, ("stage", stage), ("reason", reason));
        else
            BppLog.WarnEvent(logEvent, exception, ("stage", stage), ("reason", reason));
    }

    // The content list scrolls by drag, as the game's own; a position that settled is logged.
    private void ObserveScroll(float now)
    {
        if (_scrollRect == null)
            return;
        var position = _scrollRect.verticalNormalizedPosition;
        if (float.IsNaN(_scrollPosition) || Math.Abs(position - _scrollPosition) > 0.001f)
        {
            _scrollPosition = position;
            _scrollMovedAt = now;
            return;
        }
        if (now - _scrollMovedAt < 0.3f)
            return;
        if (
            !float.IsNaN(_loggedScrollPosition)
            && Math.Abs(position - _loggedScrollPosition) < 0.02f
        )
            return;
        var from = _loggedScrollPosition;
        _loggedScrollPosition = position;
        if (float.IsNaN(from))
            return;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.settings.scrolled"),
            ("from", Math.Round(from, 3)),
            ("to", Math.Round(position, 3)),
            ("dragging", _scrollRect.velocity.sqrMagnitude > 0f)
        );
    }

    // The section's size, the touch geometry last applied to it, and whether the navigation
    // column still holds every entry inside the split container now that BazaarPlusPlus is added.
    private static void LogVisibility(AndroidNativeSettingsSection section, string state)
    {
        var rect = section._sectionRoot.rect;
        var (entries, listHeight, inside) = section.MeasureNavigation();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.settings.section_visibility"),
            ("state", state),
            ("screen", $"{Screen.width}x{Screen.height}"),
            ("section_width", Math.Round(rect.width)),
            ("section_height", Math.Round(rect.height)),
            ("rows", section._rows.Count),
            ("min_hit_px", Math.Round(section._lastMinHitPx)),
            ("min_option_px", Math.Round(section._lastMinOptionPx)),
            ("hit_overlaps", section._lastHitOverlaps),
            ("nav_entries", entries),
            ("nav_list_height", Math.Round(listHeight)),
            ("nav_inside", inside)
        );
    }

    // The navigation column: its active entries, the pixels they span, and whether the lowest
    // entry's bottom edge lies inside the split container (grown into the hidden footer's space).
    private (int Entries, float ListHeight, bool Inside) MeasureNavigation()
    {
        var list =
            _navigationRoot == null ? null : _navigationRoot.parent?.TryCast<RectTransform>();
        if (list == null)
            return (0, 0f, false);
        var entries = 0;
        var lowest = float.PositiveInfinity;
        var highest = float.NegativeInfinity;
        for (var index = 0; index < list.childCount; index++)
        {
            var child = list.GetChild(index)?.TryCast<RectTransform>();
            if (child == null || !child.gameObject.activeSelf)
                continue;
            entries++;
            var span = ScreenVerticalSpan(child);
            lowest = Math.Min(lowest, span.Min);
            highest = Math.Max(highest, span.Max);
        }
        if (entries == 0)
            return (0, 0f, false);
        var split = _dialog == null ? null : SplitContainer(_dialog);
        var inside = split != null && lowest >= ScreenVerticalSpan(split).Min - 0.5f;
        // From the top entry's top edge to the lowest entry's bottom edge, in screen pixels.
        return (entries, highest - lowest, inside);
    }

    // A rect's vertical extent in screen pixels, through its canvas's camera (none on an
    // overlay canvas).
    private static (float Min, float Max) ScreenVerticalSpan(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>(true)?.rootCanvas;
        var camera =
            canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;
        var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4L);
        rect.GetWorldCorners(corners);
        var bottom = RectTransformUtility.WorldToScreenPoint(camera, corners[0]).y;
        var top = RectTransformUtility.WorldToScreenPoint(camera, corners[1]).y;
        return (Math.Min(bottom, top), Math.Max(bottom, top));
    }

    private static string CurrentLanguageCode
    {
        get
        {
            try
            {
                return PlayerPreferences.Data?.LanguageCode ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    private sealed class RowView(
        BppSettingsDockDefinition definition,
        RectTransform root,
        Toggle? toggle,
        TMP_Dropdown? choice,
        Button? action
    )
    {
        public BppSettingsDockDefinition Definition { get; } = definition;
        public RectTransform Root { get; } = root;
        public Toggle? Toggle { get; } = toggle;
        public TMP_Dropdown? Choice { get; } = choice;
        public Button? Action { get; } = action;

        /// <summary>The dropdown template's geometry as cloned, before any fit.</summary>
        public OptionsGeometry? NativeOptions { get; set; }
    }

    private sealed record OptionsGeometry(
        Vector2 TemplateSize,
        Vector2 ContentSize,
        Vector2 ContentPosition,
        Vector2 ItemSize,
        Vector2 ItemPosition
    );

    // What the install changed outside the section, undone if a later stage fails.
    private sealed class FooterRollback
    {
        private RectTransform? _splitContainer;
        private Vector2 _offsetMin;
        private Vector2 _offsetMax;
        private Vector2 _anchoredPosition;

        public List<(GameObject Target, bool WasActive)> Hidden { get; } = new();

        public bool SplitContainerRestored { get; private set; }

        public void RecordSplitContainer(RectTransform splitContainer)
        {
            if (_splitContainer != null)
                return;
            _splitContainer = splitContainer;
            _offsetMin = splitContainer.offsetMin;
            _offsetMax = splitContainer.offsetMax;
            _anchoredPosition = splitContainer.anchoredPosition;
        }

        /// <summary>Shows the hidden footer again and returns the container to its frame.</summary>
        public int Undo()
        {
            var restored = 0;
            foreach (var (target, wasActive) in Hidden)
            {
                if (target == null)
                    continue;
                target.SetActive(wasActive);
                restored++;
            }
            if (_splitContainer != null)
            {
                _splitContainer.offsetMin = _offsetMin;
                _splitContainer.offsetMax = _offsetMax;
                _splitContainer.anchoredPosition = _anchoredPosition;
                SplitContainerRestored = true;
            }
            return restored;
        }
    }
}
