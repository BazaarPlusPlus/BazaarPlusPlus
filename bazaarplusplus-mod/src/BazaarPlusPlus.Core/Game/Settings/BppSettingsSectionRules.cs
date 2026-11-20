#nullable enable
namespace BazaarPlusPlus.Game.Settings;

// Use SameState when comparing refreshes because record equality compares Options by reference.
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

public enum BppSettingsActionStep
{
    Ignore,
    Run,
    CloseThenRun,
}

public static class BppSettingsSectionRules
{
    private static readonly BppSettingsControlKind[] KindOrder =
    {
        BppSettingsControlKind.Toggle,
        BppSettingsControlKind.Choice,
        BppSettingsControlKind.Action,
    };

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

    public static BppSettingsRowState Resolve(BppSettingsDockDefinition row, string languageCode)
    {
        // A toggle's IsActive calls ReadToggle, so reading both would invoke the same delegate twice.
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

    // The row's SelectStandardChoice already rejects indices outside its standard choices.
    public static int? StandardIndex(BppSettingsChoiceState? state, int shownIndex)
    {
        if (state == null)
            return null;
        if (!state.HasSyntheticCurrentOption)
            return shownIndex;
        return shownIndex == 0 ? null : shownIndex - 1;
    }

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
