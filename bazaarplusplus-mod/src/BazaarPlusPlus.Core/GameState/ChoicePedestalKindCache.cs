#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Core.GameState;

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

// Recheck periodically because a native selection edit can keep the same list identity and size.
public sealed class ChoicePedestalKindCache
{
    public const int RecheckFrames = 30;

    private bool _hasKind;
    private ChoicePedestalScreenKey _key;
    private int _readFrame;
    private ChoiceScreenPedestalKind _kind;

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
