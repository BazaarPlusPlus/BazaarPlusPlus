#nullable enable
using System.Reflection;
using HarmonyLib;
using TheBazaar.UI.Components;
using TheBazaar.UIScripts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class BppNativeSettingsSectionController : MonoBehaviour
{
    private const string SectionObjectName = "BPP_SettingsSection";
    private const string NavigationObjectName = "BPP_SettingsNavigationToggle";
    private const string RowObjectPrefix = "BPP_SettingsRow_";
    private const string ToggleGroupSpacerObjectName = "BPP_SettingsToggleGroupSpacer";
    private const string TogglePairObjectPrefix = "BPP_SettingsTogglePair_";
    private const string TogglePairFillerObjectName = "BPP_SettingsTogglePairFiller";
    private const float ToggleColumnSpacing = 24f;
    private const float SplitContainerExtraHeight = 24f;

    private static readonly FieldInfo? EntriesField = AccessTools.Field(
        typeof(ScrollSpyController),
        "_entries"
    );
    private static readonly FieldInfo? ScrollRectField = AccessTools.Field(
        typeof(ScrollSpyController),
        "_scrollRect"
    );
    private static readonly FieldInfo? ContactSupportButtonField = AccessTools.Field(
        typeof(OptionsDialogController),
        "ContactSupportButton"
    );
    private static readonly FieldInfo? CloseButtonField = AccessTools.Field(
        typeof(OptionsDialogController),
        "CloseButton"
    );
    private static readonly FieldInfo? AnonymousToggleField = AccessTools.Field(
        typeof(OptionsDialogController),
        "_anonymousModeToggle"
    );
    private static readonly FieldInfo? ResolutionDropdownField = AccessTools.Field(
        typeof(OptionsDialogController),
        "ResolutionDropdown"
    );

    private readonly List<NativeRowView> _rows = [];
    private OptionsDialogController? _optionsDialog;
    private Button? _closeButton;
    private RectTransform? _sectionRoot;
    private RectTransform? _navigationRoot;
    private int _lastPresentationHash;
    private float _nextStateProbeAt;

    internal static void TryInstall(ScrollSpyController scrollSpy)
    {
        if (scrollSpy == null || EntriesField == null || ScrollRectField == null)
            return;

        var optionsDialog = scrollSpy.GetComponentInParent<OptionsDialogController>();
        if (optionsDialog == null)
            return;

        if (optionsDialog.GetComponent<BppNativeSettingsSectionController>() != null)
            return;

        RectTransform? stagedSection = null;
        RectTransform? stagedNavigation = null;
        ScrollSpyEntry[]? originalEntries = null;
        IReadOnlyList<FooterMutation>? footerMutations = null;
        var entriesCommitted = false;
        var logAttempt = new NativeSettingsInstallLogAttempt();
        try
        {
            var entries = EntriesField.GetValue(scrollSpy) as ScrollSpyEntry[];
            var scrollRect = ScrollRectField.GetValue(scrollSpy) as ScrollRect;
            var contactButton = ContactSupportButtonField?.GetValue(optionsDialog) as Button;
            var closeButton = CloseButtonField?.GetValue(optionsDialog) as Button;
            var toggleDonor = AnonymousToggleField?.GetValue(optionsDialog) as Toggle;
            var choiceDonor = ResolutionDropdownField?.GetValue(optionsDialog) as TMP_Dropdown;
            if (
                entries == null
                || entries.Length == 0
                || scrollRect?.content == null
                || contactButton == null
                || closeButton == null
                || toggleDonor == null
                || choiceDonor == null
            )
            {
                NativeSettingsLogState.ReportInstallFailure(
                    SettingsNativeSectionStage.BuildSection,
                    SettingsLogReasonCode.SupportSectionUnavailable
                );
                return;
            }
            originalEntries = entries;

            var supportSection = FindDirectChildAncestor(
                scrollRect.content,
                contactButton.transform
            );
            if (supportSection == null)
            {
                NativeSettingsLogState.ReportInstallFailure(
                    SettingsNativeSectionStage.ResolveSupportSection,
                    SettingsLogReasonCode.SupportSectionUnavailable
                );
                return;
            }

            var supportIndex = FindEntryIndex(entries, supportSection);
            if (supportIndex < 0 || entries[supportIndex].NavButtonRoot == null)
            {
                NativeSettingsLogState.ReportInstallFailure(
                    SettingsNativeSectionStage.ResolveScrollSpyEntry,
                    SettingsLogReasonCode.ScrollSpyEntryUnavailable
                );
                return;
            }

            var supportNavigation = entries[supportIndex].NavButtonRoot;
            stagedSection = CloneInactive(supportSection, supportSection.parent, SectionObjectName);
            stagedNavigation = CloneInactive(
                supportNavigation,
                supportNavigation.parent,
                NavigationObjectName
            );
            if (stagedSection == null || stagedNavigation == null)
                throw new InvalidOperationException("Failed to stage native settings clones.");

            ConfigureNavigation(stagedNavigation);
            var controller =
                optionsDialog.gameObject.AddComponent<BppNativeSettingsSectionController>();
            controller._optionsDialog = optionsDialog;
            controller._closeButton = closeButton;
            controller._sectionRoot = stagedSection;
            controller._navigationRoot = stagedNavigation;
            controller.BuildSection(
                stagedSection,
                scrollRect.content,
                contactButton,
                toggleDonor,
                choiceDonor
            );
            footerMutations = controller.HideNativeFooter(supportSection, logAttempt);

            var expandedEntries = new ScrollSpyEntry[entries.Length + 1];
            Array.Copy(entries, 0, expandedEntries, 0, supportIndex + 1);
            expandedEntries[supportIndex + 1] = new ScrollSpyEntry
            {
                NavButtonRoot = stagedNavigation,
                SectionRoot = stagedSection,
            };
            Array.Copy(
                entries,
                supportIndex + 1,
                expandedEntries,
                supportIndex + 2,
                entries.Length - supportIndex - 1
            );

            stagedSection.SetSiblingIndex(supportSection.GetSiblingIndex() + 1);
            stagedNavigation.SetSiblingIndex(supportNavigation.GetSiblingIndex() + 1);
            EntriesField.SetValue(scrollSpy, expandedEntries);
            entriesCommitted = true;
            stagedSection.gameObject.SetActive(true);
            stagedNavigation.gameObject.SetActive(true);
            controller.RebuildLayout();
            controller.RefreshView(force: true);
            logAttempt.CommitSuccess();
            stagedSection = null;
            stagedNavigation = null;
        }
        catch (Exception ex)
        {
            if (entriesCommitted && originalEntries != null)
                EntriesField.SetValue(scrollSpy, originalEntries);
            if (footerMutations != null)
            {
                foreach (var mutation in footerMutations)
                    mutation.Target.SetActive(mutation.WasActive);
            }
            if (stagedSection != null)
                DestroyImmediate(stagedSection.gameObject);
            if (stagedNavigation != null)
                DestroyImmediate(stagedNavigation.gameObject);

            var controller = optionsDialog.GetComponent<BppNativeSettingsSectionController>();
            if (controller != null)
                DestroyImmediate(controller);
            NativeSettingsLogState.ReportInstallFailure(
                SettingsNativeSectionStage.Install,
                SettingsLogReasonCode.InstallException,
                ex
            );
        }
    }

    internal static void RefreshAll()
    {
        foreach (
            var controller in FindObjectsOfType<BppNativeSettingsSectionController>(
                includeInactive: true
            )
        )
            controller.RefreshView(force: true);
    }

    private void OnEnable()
    {
        RebuildLayout();
        RefreshView(force: true);
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextStateProbeAt)
            return;

        _nextStateProbeAt = Time.unscaledTime + 0.5f;
        RefreshView(force: false);
    }

    private void BuildSection(
        RectTransform sectionRoot,
        RectTransform content,
        Button actionDonor,
        Toggle toggleDonor,
        TMP_Dropdown choiceDonor
    )
    {
        StripNativeLocalization(sectionRoot);
        SetSectionHeader(sectionRoot);
        RemoveDonorRows(sectionRoot);
        RemoveDonorArtifacts(sectionRoot);

        foreach (
            var kind in new[]
            {
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Choice,
                BppSettingsControlKind.Action,
            }
        )
        {
            foreach (var definition in BppSettingsDockCatalog.Definitions)
            {
                if (definition.ControlKind != kind)
                    continue;

                var sourceControl = definition.ControlKind switch
                {
                    BppSettingsControlKind.Toggle => (Selectable)toggleDonor,
                    BppSettingsControlKind.Choice => choiceDonor,
                    _ => actionDonor,
                };
                var sourceSection = FindDirectChildAncestor(content, sourceControl.transform);
                if (sourceSection == null)
                    throw new InvalidOperationException(
                        $"No source section for '{definition.Key}'."
                    );

                var sourceRow = FindSingleSelectableRow(sourceSection, sourceControl);
                if (sourceRow == null)
                    throw new InvalidOperationException($"No source row for '{definition.Key}'.");

                var rowObject = Instantiate(
                    sourceRow.gameObject,
                    sectionRoot,
                    worldPositionStays: false
                );
                rowObject.name = RowObjectPrefix + definition.Key;
                rowObject.SetActive(true);
                StripNativeLocalization(rowObject.transform);
                var rowRect = rowObject.GetComponent<RectTransform>();
                if (rowRect == null)
                    throw new InvalidOperationException(
                        $"Row '{definition.Key}' has no RectTransform."
                    );

                var view = ConfigureRow(definition, rowRect);
                _rows.Add(view);
            }
        }

        GroupToggleRows(sectionRoot);
    }

    private NativeRowView ConfigureRow(BppSettingsDockDefinition definition, RectTransform rowRoot)
    {
        Toggle? toggle = null;
        TMP_Dropdown? choice = null;
        Button? action = null;
        switch (definition.ControlKind)
        {
            case BppSettingsControlKind.Toggle:
                toggle = rowRoot.GetComponentInChildren<Toggle>(true);
                if (toggle == null)
                    throw new InvalidOperationException(
                        $"Toggle donor failed for '{definition.Key}'."
                    );
                toggle.onValueChanged = new Toggle.ToggleEvent();
                toggle.onValueChanged.AddListener(enabled =>
                {
                    definition.WriteToggle?.Invoke(enabled);
                    RefreshView(force: true);
                });
                RemoveOtherSelectables(rowRoot, toggle);
                break;
            case BppSettingsControlKind.Choice:
                choice = rowRoot.GetComponentInChildren<TMP_Dropdown>(true);
                if (choice == null)
                    throw new InvalidOperationException(
                        $"Choice donor failed for '{definition.Key}'."
                    );
                choice.onValueChanged = new TMP_Dropdown.DropdownEvent();
                choice.onValueChanged.AddListener(selectedIndex =>
                {
                    var state = definition.ResolveChoiceState?.Invoke(CurrentLanguageCode);
                    if (state == null)
                        return;

                    if (state.HasSyntheticCurrentOption && selectedIndex == 0)
                        return;

                    definition.SelectStandardChoice?.Invoke(
                        selectedIndex - (state.HasSyntheticCurrentOption ? 1 : 0)
                    );
                    RefreshView(force: true);
                });
                RemoveOtherSelectables(rowRoot, choice);
                break;
            default:
                action = rowRoot.GetComponentInChildren<Button>(true);
                if (action == null)
                    throw new InvalidOperationException(
                        $"Action donor failed for '{definition.Key}'."
                    );
                action.onClick = new Button.ButtonClickedEvent();
                action.onClick.AddListener(() => ActivateAction(definition));
                RemoveOtherSelectables(rowRoot, action);
                break;
        }

        return new NativeRowView(definition, rowRoot, toggle, choice, action);
    }

    private void ActivateAction(BppSettingsDockDefinition definition)
    {
        if (!definition.IsActive())
            return;

        if (definition.CollapseAfterActivate)
            _closeButton?.onClick.Invoke();

        definition.Activate?.Invoke();
        RefreshView(force: true);
    }

    private void RefreshView(bool force)
    {
        if (_rows.Count == 0)
            return;

        var languageCode = CurrentLanguageCode;
        var presentationHash = 17;
        foreach (var row in _rows)
        {
            presentationHash = (presentationHash * 31) ^ row.Definition.Key.GetHashCode();
            presentationHash = (presentationHash * 31) ^ row.Definition.IsActive().GetHashCode();
            switch (row.Definition.ControlKind)
            {
                case BppSettingsControlKind.Toggle:
                    presentationHash =
                        (presentationHash * 31)
                        ^ (row.Definition.ReadToggle?.Invoke() == true).GetHashCode();
                    presentationHash =
                        (presentationHash * 31)
                        ^ (row.Definition.IsInteractable?.Invoke() != false).GetHashCode();
                    break;
                case BppSettingsControlKind.Choice:
                {
                    var state = row.Definition.ResolveChoiceState?.Invoke(languageCode);
                    if (state == null)
                        break;
                    presentationHash = (presentationHash * 31) ^ state.SelectedIndex;
                    foreach (var option in state.Options)
                        presentationHash = (presentationHash * 31) ^ option.GetHashCode();
                    break;
                }
            }
        }

        if (!force && presentationHash == _lastPresentationHash)
            return;

        _lastPresentationHash = presentationHash;
        foreach (var row in _rows)
        {
            row.Root.gameObject.SetActive(true);
            ApplyRow(row, languageCode);
        }
    }

    private static void ApplyRow(NativeRowView row, string languageCode)
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
                    Mathf.Clamp(state.SelectedIndex, 0, Math.Max(0, row.Choice.options.Count - 1))
                );
                row.Choice.RefreshShownValue();
                row.Choice.interactable = true;
                break;
            }
            case BppSettingsControlKind.Action when row.Action != null:
                row.Action.interactable = row.Definition.IsActive();
                break;
        }
    }

    private IReadOnlyList<FooterMutation> HideNativeFooter(
        RectTransform supportSection,
        NativeSettingsInstallLogAttempt logAttempt
    )
    {
        var mutations = new List<FooterMutation>();
        if (_optionsDialog == null)
            return mutations;

        var dialogRoot = _optionsDialog.transform as RectTransform;
        var splitContainer =
            _optionsDialog.transform.Find("ScalerOffset/SplitContainer") as RectTransform;
        if (dialogRoot == null || splitContainer == null)
        {
            logAttempt.ObserveLayoutFailure(
                SettingsNativeLayoutOperation.FooterGeometry,
                SettingsLogReasonCode.GeometryUnavailable
            );
            return mutations;
        }
        logAttempt.ObserveLayoutSuccess(
            SettingsNativeLayoutOperation.FooterGeometry,
            SettingsNativeLayoutOutcome.Applied,
            affectedCount: 1
        );

        var scalerOffset = splitContainer.parent;
        var footerLabel = scalerOffset?.Find("Notice_ChangeAuto")?.gameObject;
        var divider = scalerOffset?.Find("Divider_1")?.gameObject;
        if (footerLabel != null)
            HideFooterObject(footerLabel, mutations);
        if (divider != null)
            HideFooterObject(divider, mutations);

        var footerMutationCount = mutations.Count;
        if (footerMutationCount == 0)
        {
            logAttempt.ObserveLayoutFailure(
                SettingsNativeLayoutOperation.FooterSiblings,
                SettingsLogReasonCode.GeometryUnavailable
            );
        }
        else
        {
            logAttempt.ObserveLayoutSuccess(
                SettingsNativeLayoutOperation.FooterSiblings,
                SettingsNativeLayoutOutcome.Applied,
                footerMutationCount
            );
        }

        var supportBuffer = supportSection.Find("Buffer")?.gameObject;
        if (supportBuffer != null)
        {
            HideFooterObject(supportBuffer, mutations);
            logAttempt.ObserveLayoutSuccess(
                SettingsNativeLayoutOperation.SupportBottomBuffer,
                SettingsNativeLayoutOutcome.Applied,
                affectedCount: 1
            );
        }
        else
        {
            logAttempt.ObserveLayoutFailure(
                SettingsNativeLayoutOperation.SupportBottomBuffer,
                SettingsLogReasonCode.SupportBufferUnavailable
            );
        }

        var expansion = ExpandSplitContainer(splitContainer, footerLabel, divider);
        logAttempt.ObserveLayoutSuccess(
            SettingsNativeLayoutOperation.SplitContainer,
            expansion.Applied
                ? SettingsNativeLayoutOutcome.Applied
                : SettingsNativeLayoutOutcome.Skipped,
            expansion.Applied ? 1 : 0,
            expansion.GrowthUnits
        );
        LayoutRebuilder.ForceRebuildLayoutImmediate(dialogRoot);
        return mutations;
    }

    private static SplitContainerExpansion ExpandSplitContainer(
        RectTransform splitContainer,
        GameObject? footerLabel,
        GameObject? divider
    )
    {
        if (splitContainer.parent is not RectTransform parent)
            return default;

        var targetBottomY = float.PositiveInfinity;
        foreach (var footerObject in new[] { footerLabel, divider })
        {
            if (footerObject?.transform is not RectTransform footerRect)
                continue;

            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                parent,
                footerRect
            );
            targetBottomY = Math.Min(targetBottomY, bounds.min.y);
        }

        if (!float.IsFinite(targetBottomY))
            return default;

        var corners = new Vector3[4];
        splitContainer.GetWorldCorners(corners);
        var currentBottomY = parent.InverseTransformPoint(corners[0]).y;
        var releasedHeight = currentBottomY - targetBottomY;
        if (releasedHeight <= 0f)
            return default;

        var totalGrowth = releasedHeight + SplitContainerExtraHeight;
        var halfGrowth = totalGrowth * 0.5f;
        splitContainer.offsetMin = new Vector2(
            splitContainer.offsetMin.x,
            splitContainer.offsetMin.y - halfGrowth
        );
        splitContainer.offsetMax = new Vector2(
            splitContainer.offsetMax.x,
            splitContainer.offsetMax.y + halfGrowth
        );
        splitContainer.anchoredPosition = new Vector2(splitContainer.anchoredPosition.x, 0f);
        return new SplitContainerExpansion(true, totalGrowth);
    }

    private readonly record struct SplitContainerExpansion(bool Applied, float GrowthUnits);

    private void RebuildLayout()
    {
        if (_sectionRoot == null)
            return;

        for (var current = _sectionRoot; current != null; current = current.parent as RectTransform)
            LayoutRebuilder.ForceRebuildLayoutImmediate(current);
    }

    private static void ConfigureNavigation(RectTransform navigationRoot)
    {
        StripNativeLocalization(navigationRoot);
        var toggle = navigationRoot.GetComponent<Toggle>();
        if (toggle == null)
            throw new InvalidOperationException(
                "Native support navigation donor was not a Toggle."
            );

        toggle.onValueChanged = new Toggle.ToggleEvent();
        toggle.SetIsOnWithoutNotify(false);
        foreach (var label in navigationRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            label.fontStyle &= ~FontStyles.UpperCase;
            label.text = "BazaarPlusPlus";
        }
    }

    private static void SetSectionHeader(RectTransform sectionRoot)
    {
        TextMeshProUGUI? best = null;
        foreach (var label in sectionRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label.GetComponentInParent<Selectable>() != null)
                continue;
            if (best == null || label.fontSize > best.fontSize)
                best = label;
        }

        if (best != null)
            best.text = "BazaarPlusPlus";
    }

    private static void SetRowLabel(NativeRowView row, string labelText)
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
            best.text = labelText;
    }

    private static void RemoveDonorRows(RectTransform sectionRoot)
    {
        var toRemove = new List<GameObject>();
        for (var index = 0; index < sectionRoot.childCount; index++)
        {
            var child = sectionRoot.GetChild(index);
            if (
                child.GetComponentInChildren<Button>(true) != null
                || child.GetComponentInChildren<Toggle>(true) != null
                || child.GetComponentInChildren<TMP_Dropdown>(true) != null
            )
                toRemove.Add(child.gameObject);
        }

        foreach (var child in toRemove)
            DestroyImmediate(child);
    }

    private static void RemoveDonorArtifacts(RectTransform sectionRoot)
    {
        TextMeshProUGUI? header = null;
        foreach (var label in sectionRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label.GetComponentInParent<Selectable>() != null)
                continue;
            if (header == null || label.fontSize > header.fontSize)
                header = label;
        }

        var headerRoot =
            header == null ? null : FindDirectChildAncestor(sectionRoot, header.transform);
        var toRemove = new List<GameObject>();
        for (var index = 0; index < sectionRoot.childCount; index++)
        {
            var child = sectionRoot.GetChild(index);
            if (
                child != headerRoot
                && !child.name.StartsWith(RowObjectPrefix, StringComparison.Ordinal)
            )
                toRemove.Add(child.gameObject);
        }

        foreach (var child in toRemove)
            DestroyImmediate(child);
    }

    private static void RemoveOtherSelectables(RectTransform rowRoot, Selectable keep)
    {
        foreach (var selectable in rowRoot.GetComponentsInChildren<Selectable>(true))
        {
            if (selectable != keep && !selectable.transform.IsChildOf(keep.transform))
                DestroyImmediate(selectable);
        }
    }

    private void GroupToggleRows(RectTransform sectionRoot)
    {
        var pairIndex = 0;
        var controlKinds = new BppSettingsControlKind[_rows.Count];
        for (var index = 0; index < _rows.Count; index++)
            controlKinds[index] = _rows[index].Definition.ControlKind;

        foreach (var group in PlanToggleRowGroups(controlKinds))
        {
            var left = _rows[group.LeftIndex];
            var right = group.RightIndex.HasValue ? _rows[group.RightIndex.Value] : null;

            var preferredHeight =
                right == null
                    ? ResolveRowHeight(left.Root)
                    : Math.Max(ResolveRowHeight(left.Root), ResolveRowHeight(right.Root));
            var siblingIndex = left.Root.GetSiblingIndex();
            if (pairIndex == 0)
                CreateToggleGroupSpacer(sectionRoot, siblingIndex++);

            var pairObject = new GameObject(
                TogglePairObjectPrefix + pairIndex,
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup),
                typeof(LayoutElement)
            );
            var pairRect = pairObject.GetComponent<RectTransform>();
            pairRect.SetParent(sectionRoot, worldPositionStays: false);
            pairRect.SetSiblingIndex(siblingIndex);

            var pairLayout = pairObject.GetComponent<HorizontalLayoutGroup>();
            pairLayout.spacing = ToggleColumnSpacing;
            pairLayout.childAlignment = TextAnchor.UpperLeft;
            pairLayout.childControlWidth = true;
            pairLayout.childControlHeight = true;
            pairLayout.childForceExpandWidth = true;
            pairLayout.childForceExpandHeight = false;

            var pairElement = pairObject.GetComponent<LayoutElement>();
            pairElement.minHeight = preferredHeight;
            pairElement.preferredHeight = preferredHeight;
            pairElement.flexibleWidth = 1f;

            left.Root.SetParent(pairRect, worldPositionStays: false);
            if (right != null)
                right.Root.SetParent(pairRect, worldPositionStays: false);
            else
                CreateTogglePairFiller(pairRect, left.Root);
            pairIndex++;
        }
    }

    internal static IReadOnlyList<(int LeftIndex, int? RightIndex)> PlanToggleRowGroups(
        IReadOnlyList<BppSettingsControlKind> controlKinds
    )
    {
        var groups = new List<(int LeftIndex, int? RightIndex)>();
        for (var index = 0; index < controlKinds.Count; index += 2)
        {
            if (controlKinds[index] != BppSettingsControlKind.Toggle)
                break;

            var rightIndex =
                index + 1 < controlKinds.Count
                && controlKinds[index + 1] == BppSettingsControlKind.Toggle
                    ? index + 1
                    : (int?)null;
            groups.Add((index, rightIndex));
            if (!rightIndex.HasValue)
                break;
        }

        return groups;
    }

    private static void CreateTogglePairFiller(RectTransform pairRoot, RectTransform leftRow)
    {
        var fillerObject = new GameObject(
            TogglePairFillerObjectName,
            typeof(RectTransform),
            typeof(LayoutElement)
        );
        var fillerRect = fillerObject.GetComponent<RectTransform>();
        fillerRect.SetParent(pairRoot, worldPositionStays: false);

        var fillerElement = fillerObject.GetComponent<LayoutElement>();
        fillerElement.minWidth = LayoutUtility.GetMinWidth(leftRow);
        fillerElement.preferredWidth = LayoutUtility.GetPreferredWidth(leftRow);
        fillerElement.flexibleWidth = LayoutUtility.GetFlexibleWidth(leftRow);
    }

    private static void CreateToggleGroupSpacer(RectTransform sectionRoot, int siblingIndex)
    {
        var spacerObject = new GameObject(
            ToggleGroupSpacerObjectName,
            typeof(RectTransform),
            typeof(LayoutElement)
        );
        var spacerRect = spacerObject.GetComponent<RectTransform>();
        spacerRect.SetParent(sectionRoot, worldPositionStays: false);
        spacerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 0f);
        spacerRect.SetSiblingIndex(siblingIndex);

        var spacerElement = spacerObject.GetComponent<LayoutElement>();
        spacerElement.minHeight = 0f;
        spacerElement.preferredHeight = 0f;
        spacerElement.flexibleHeight = 0f;
    }

    private static float ResolveRowHeight(RectTransform row)
    {
        var height = LayoutUtility.GetPreferredHeight(row);
        return height > 0f ? height : Math.Max(1f, row.rect.height);
    }

    private static void StripNativeLocalization(Transform root)
    {
        foreach (var localizable in root.GetComponentsInChildren<LocalizableTextComponent>(true))
            DestroyImmediate(localizable);
    }

    private static void HideFooterObject(GameObject target, ICollection<FooterMutation> mutations)
    {
        foreach (var mutation in mutations)
        {
            if (mutation.Target == target)
                return;
        }

        mutations.Add(new FooterMutation(target, target.activeSelf));
        target.SetActive(false);
    }

    private static IEnumerable<string> BuildMutationPaths(IEnumerable<FooterMutation> mutations)
    {
        foreach (var mutation in mutations)
            yield return BuildPath(mutation.Target.transform);
    }

    private static RectTransform? CloneInactive(RectTransform source, Transform parent, string name)
    {
        var clone = Instantiate(source.gameObject, parent, worldPositionStays: false);
        clone.name = name;
        clone.SetActive(false);
        return clone.GetComponent<RectTransform>();
    }

    private static RectTransform? FindDirectChildAncestor(Transform parent, Transform descendant)
    {
        for (
            var current = descendant;
            current != null && current != parent;
            current = current.parent
        )
        {
            if (current.parent == parent)
                return current as RectTransform;
        }

        return null;
    }

    private static RectTransform? FindSingleSelectableRow(
        RectTransform sectionRoot,
        Selectable donor
    )
    {
        var current = donor.transform as RectTransform;
        if (current == null)
            return null;

        var best = current;
        while (current.parent != null && current.parent != sectionRoot)
        {
            if (current.parent is not RectTransform parent)
                break;
            if (ContainsSelectableOutsideDonor(parent, donor))
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
            if (selectable == donor || selectable.transform.IsChildOf(donor.transform))
                continue;

            return true;
        }

        return false;
    }

    private static int FindEntryIndex(ScrollSpyEntry[] entries, RectTransform sectionRoot)
    {
        for (var index = 0; index < entries.Length; index++)
        {
            if (entries[index].SectionRoot == sectionRoot)
                return index;
        }

        return -1;
    }

    private static string BuildPath(Transform transform)
    {
        var path = transform.name;
        for (var current = transform.parent; current != null; current = current.parent)
            path = current.name + "/" + path;
        return path;
    }

    private static string CurrentLanguageCode => PlayerPreferences.Data.LanguageCode;

    private readonly struct FooterMutation(GameObject target, bool wasActive)
    {
        internal GameObject Target { get; } = target;
        internal bool WasActive { get; } = wasActive;
    }

    private sealed class NativeRowView(
        BppSettingsDockDefinition definition,
        RectTransform root,
        Toggle? toggle,
        TMP_Dropdown? choice,
        Button? action
    )
    {
        internal BppSettingsDockDefinition Definition { get; } = definition;
        internal RectTransform Root { get; } = root;
        internal Toggle? Toggle { get; } = toggle;
        internal TMP_Dropdown? Choice { get; } = choice;
        internal Button? Action { get; } = action;
    }
}
