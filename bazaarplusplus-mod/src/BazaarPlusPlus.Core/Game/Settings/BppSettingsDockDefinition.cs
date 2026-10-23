#nullable enable
namespace BazaarPlusPlus.Game.Settings;

public enum BppSettingsControlKind
{
    Toggle,
    Choice,
    Action,
}

public sealed class BppSettingsChoiceState
{
    public BppSettingsChoiceState(
        IReadOnlyList<string> options,
        int selectedIndex,
        bool hasSyntheticCurrentOption
    )
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        SelectedIndex = selectedIndex;
        HasSyntheticCurrentOption = hasSyntheticCurrentOption;
    }

    public IReadOnlyList<string> Options { get; }

    public int SelectedIndex { get; }

    public bool HasSyntheticCurrentOption { get; }
}

public sealed class BppSettingsDockDefinition
{
    public BppSettingsDockDefinition(
        string key,
        Func<string, string> resolveLabel,
        Func<bool> isActive,
        Action activate,
        bool collapseAfterActivate
    )
        : this(key, resolveLabel, isActive)
    {
        Activate = activate ?? throw new ArgumentNullException(nameof(activate));
        CollapseAfterActivate = collapseAfterActivate;
        ControlKind = BppSettingsControlKind.Action;
    }

    private BppSettingsDockDefinition(
        string key,
        Func<string, string> resolveLabel,
        Func<bool> isActive
    )
    {
        Key = !string.IsNullOrWhiteSpace(key)
            ? key
            : throw new ArgumentException("Key is required.", nameof(key));
        ResolveLabel = resolveLabel ?? throw new ArgumentNullException(nameof(resolveLabel));
        IsActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
    }

    private BppSettingsDockDefinition(
        string key,
        Func<string, string> resolveLabel,
        Func<bool> isActive,
        BppSettingsControlKind controlKind,
        Func<bool>? readToggle,
        Action<bool>? writeToggle,
        Func<bool>? isInteractable,
        Func<string, BppSettingsChoiceState>? resolveChoiceState,
        Action<int>? selectStandardChoice
    )
        : this(key, resolveLabel, isActive)
    {
        ControlKind = controlKind;
        ReadToggle = readToggle;
        WriteToggle = writeToggle;
        IsInteractable = isInteractable;
        ResolveChoiceState = resolveChoiceState;
        SelectStandardChoice = selectStandardChoice;
    }

    public string Key { get; }

    public Func<string, string> ResolveLabel { get; }

    public Func<bool> IsActive { get; }

    public Action? Activate { get; }

    public bool CollapseAfterActivate { get; }

    public BppSettingsControlKind ControlKind { get; private set; }

    public Func<bool>? ReadToggle { get; }

    public Action<bool>? WriteToggle { get; }

    public Func<bool>? IsInteractable { get; }

    public Func<string, BppSettingsChoiceState>? ResolveChoiceState { get; }

    public Action<int>? SelectStandardChoice { get; }

    public static BppSettingsDockDefinition Toggle(
        string key,
        Func<string, string> resolveLabel,
        Func<bool> read,
        Action<bool> write,
        Func<bool>? isInteractable = null
    ) =>
        new(
            key,
            resolveLabel,
            read,
            BppSettingsControlKind.Toggle,
            read,
            write,
            isInteractable ?? (() => true),
            resolveChoiceState: null,
            selectStandardChoice: null
        );

    public static BppSettingsDockDefinition Choice(
        string key,
        Func<string, string> resolveLabel,
        Func<bool> isActive,
        Func<string, BppSettingsChoiceState> resolveChoiceState,
        Action<int> selectStandardChoice
    ) =>
        new(
            key,
            resolveLabel,
            isActive,
            BppSettingsControlKind.Choice,
            readToggle: null,
            writeToggle: null,
            isInteractable: () => true,
            resolveChoiceState,
            selectStandardChoice
        );
}
