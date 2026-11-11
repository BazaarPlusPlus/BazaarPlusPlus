#nullable enable
namespace BazaarPlusPlus.Game.Settings;

/// <summary>
/// What one row of the BazaarPlusPlus section shows, resolved once per refresh: the host writes
/// it onto its cloned control. <see cref="Resolved"/> is false only for a choice whose state
/// did not resolve; the host then leaves that dropdown as it is.
/// </summary>
/// <remarks>
/// Compare two states with <see cref="BppSettingsSectionRules.SameState"/>: the record's own
/// equality compares <see cref="Options"/> by reference.
/// </remarks>
public sealed record BppSettingsRowState(
    string Key,
    BppSettingsControlKind Kind,
    bool Visible,
    string Label,
    bool ToggleOn,
    bool Interactable,
    IReadOnlyList<string> Options,
    int SelectedIndex,
    bool Resolved
);

/// <summary>What a tap on an action row does.</summary>
public enum BppSettingsActionStep
{
    Ignore,
    Run,
    CloseThenRun,
}

/// <summary>
/// The decisions of the BazaarPlusPlus section in the game's settings dialog, shared by both
/// hosts: the row order, each row's state, change detection, the dropdown index and the action
/// tap. The hosts read the language code, clone the native rows, and write these states onto
/// them. No catch here: an exception from a definition's delegate reaches the host.
/// </summary>
public static class BppSettingsSectionRules
{
    private static readonly BppSettingsControlKind[] KindOrder =
    {
        BppSettingsControlKind.Toggle,
        BppSettingsControlKind.Choice,
        BppSettingsControlKind.Action,
    };

    /// <summary>Toggles, then choices, then actions; catalog order inside each kind.</summary>
    public static IReadOnlyList<BppSettingsDockDefinition> BuildOrder(
        IReadOnlyList<BppSettingsDockDefinition> rows
    )
    {
        var ordered = new List<BppSettingsDockDefinition>(rows.Count);
        foreach (var kind in KindOrder)
        {
            foreach (var row in rows)
            {
                if (row.ControlKind == kind)
                    ordered.Add(row);
            }
        }
        return ordered;
    }

    /// <summary>
    /// Calls each of the row's delegates once, the invisible row's too, so that a change to it
    /// still counts in <see cref="SameState"/>.
    /// </summary>
    public static BppSettingsRowState Resolve(BppSettingsDockDefinition row, string languageCode)
    {
        // The order the hosts' presentation hash read them in: visibility, the active read
        // (a toggle's IsActive is its ReadToggle, so it is read once), the label, the rest.
        var visible = row.IsVisible(languageCode);
        var active = row.ControlKind switch
        {
            BppSettingsControlKind.Toggle => row.ReadToggle?.Invoke() == true,
            BppSettingsControlKind.Action => row.IsActive(),
            _ => false,
        };
        var label = row.ResolveLabel(languageCode);
        switch (row.ControlKind)
        {
            case BppSettingsControlKind.Toggle:
                return State(
                    row,
                    visible,
                    label,
                    toggleOn: active,
                    interactable: row.IsInteractable?.Invoke() != false,
                    Array.Empty<string>(),
                    selectedIndex: 0,
                    resolved: true
                );
            case BppSettingsControlKind.Choice:
            {
                var state = row.ResolveChoiceState?.Invoke(languageCode);
                if (state == null)
                    return State(row, visible, label, false, true, Array.Empty<string>(), 0, false);
                // A copy: a definition that changes one list in place must still read as a change.
                var options = state.Options.ToArray();
                var selected = Math.Clamp(state.SelectedIndex, 0, Math.Max(0, options.Length - 1));
                return State(row, visible, label, false, true, options, selected, true);
            }
            default:
                return State(row, visible, label, false, active, Array.Empty<string>(), 0, true);
        }
    }

    /// <summary>Whether two refreshes show the same rows; options compare item by item.</summary>
    public static bool SameState(
        IReadOnlyList<BppSettingsRowState> previous,
        IReadOnlyList<BppSettingsRowState> current
    )
    {
        if (previous.Count != current.Count)
            return false;
        for (var index = 0; index < previous.Count; index++)
        {
            var a = previous[index];
            var b = current[index];
            if (
                a.Key != b.Key
                || a.Kind != b.Kind
                || a.Visible != b.Visible
                || a.Label != b.Label
                || a.ToggleOn != b.ToggleOn
                || a.Interactable != b.Interactable
                || a.SelectedIndex != b.SelectedIndex
                || a.Resolved != b.Resolved
                || !a.Options.SequenceEqual(b.Options, StringComparer.Ordinal)
            )
                return false;
        }
        return true;
    }

    /// <summary>
    /// The standard choice behind a dropdown pick, or null when the pick changes nothing: the
    /// choice did not resolve, or the pick is the synthetic current option. No range check: the
    /// row's own <see cref="BppSettingsDockDefinition.SelectStandardChoice"/> ignores an index
    /// outside its values.
    /// </summary>
    public static int? StandardIndex(BppSettingsChoiceState? state, int shownIndex)
    {
        if (state == null)
            return null;
        if (!state.HasSyntheticCurrentOption)
            return shownIndex;
        return shownIndex == 0 ? null : shownIndex - 1;
    }

    /// <summary>An inactive row ignores the tap; an active one may close the dialog first.</summary>
    public static BppSettingsActionStep OnActionTap(BppSettingsDockDefinition row)
    {
        if (!row.IsActive())
            return BppSettingsActionStep.Ignore;
        return row.CollapseAfterActivate
            ? BppSettingsActionStep.CloseThenRun
            : BppSettingsActionStep.Run;
    }

    private static BppSettingsRowState State(
        BppSettingsDockDefinition row,
        bool visible,
        string label,
        bool toggleOn,
        bool interactable,
        IReadOnlyList<string> options,
        int selectedIndex,
        bool resolved
    ) =>
        new(
            row.Key,
            row.ControlKind,
            visible,
            label,
            toggleOn,
            interactable,
            options,
            selectedIndex,
            resolved
        );
}
