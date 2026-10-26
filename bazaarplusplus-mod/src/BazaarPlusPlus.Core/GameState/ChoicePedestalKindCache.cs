#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Core.GameState;

/// <summary>What a choice screen's pedestal kind can only change with: the app state and the
/// selection list (by identity), the selection's count, and the current encounter.</summary>
public readonly record struct ChoicePedestalScreenKey(
    object? AppState,
    object? SelectionSet,
    int SelectionCount,
    string? CurrentEncounterId
)
{
    public bool Equals(ChoicePedestalScreenKey other) =>
        ReferenceEquals(AppState, other.AppState)
        && ReferenceEquals(SelectionSet, other.SelectionSet)
        && SelectionCount == other.SelectionCount
        && string.Equals(CurrentEncounterId, other.CurrentEncounterId, StringComparison.Ordinal);

    public override int GetHashCode() => SelectionCount;
}

/// <summary>Reuses the pedestal kind a per-frame reader resolved while the screen key is
/// unchanged. Reading the kind rebuilds the selection ids and pedestal snapshot, so a host that
/// polls it every frame reads it once per key, plus once every <see cref="RecheckFrames"/> frames
/// for an in-place selection edit that keeps the same list and count.</summary>
public sealed class ChoicePedestalKindCache
{
    public const int RecheckFrames = 30;

    private bool _hasKind;
    private ChoicePedestalScreenKey _key;
    private int _readFrame;
    private ChoiceScreenPedestalKind _kind;

    /// <summary>The kind matters only in <see cref="PreviewVisibilityMode.AutoOnPedestalChoice"/>
    /// with neither preview hotkey held; every other case, the default mode included, reads
    /// nothing.</summary>
    public static bool ShouldRead(PreviewVisibilityMode mode, bool holdUpgrade, bool holdEnchant) =>
        !holdUpgrade && !holdEnchant && mode == PreviewVisibilityMode.AutoOnPedestalChoice;

    public ChoiceScreenPedestalKind Resolve(
        ChoicePedestalScreenKey key,
        int frame,
        Func<ChoiceScreenPedestalKind> read
    )
    {
        if (_hasKind && key.Equals(_key) && frame - _readFrame < RecheckFrames)
            return _kind;

        _kind = read();
        _key = key;
        _readFrame = frame;
        _hasKind = true;
        return _kind;
    }
}
