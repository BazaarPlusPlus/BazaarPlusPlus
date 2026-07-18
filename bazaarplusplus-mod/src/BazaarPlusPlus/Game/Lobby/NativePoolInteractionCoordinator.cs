#nullable enable
namespace BazaarPlusPlus.Game.Lobby;

public enum NativePoolInteractionOrigin
{
    User,
    Programmatic,
}

public enum NativePoolInteractionRoute
{
    NativeAction,
    PoolEdit,
}

public static class NativePoolInteractionRouting
{
    public static NativePoolInteractionOrigin ResolveOrigin(
        bool programmaticScopeActive,
        bool nativeProgrammaticSelection
    )
    {
        return programmaticScopeActive || nativeProgrammaticSelection
            ? NativePoolInteractionOrigin.Programmatic
            : NativePoolInteractionOrigin.User;
    }

    public static bool ShouldRunNativeAction(NativePoolInteractionRoute route)
    {
        return route == NativePoolInteractionRoute.NativeAction;
    }
}

public sealed class NativePoolInteractionCoordinator<TState>
{
    private readonly Func<TState, string, bool> _isSelected;
    private readonly Func<TState, string, bool, TState> _setSelected;
    private readonly Action<TState> _persist;

    public NativePoolInteractionCoordinator(
        TState state,
        Func<TState, string, bool> isSelected,
        Func<TState, string, bool, TState> setSelected,
        Action<TState> persist
    )
    {
        State = state;
        _isSelected = isSelected;
        _setSelected = setSelected;
        _persist = persist;
    }

    public TState State { get; private set; }

    public NativePoolInteractionRoute HandleClick(
        bool poolModeEnabled,
        bool eligibleForPool,
        NativePoolInteractionOrigin origin,
        string id
    )
    {
        if (!poolModeEnabled || !eligibleForPool || origin != NativePoolInteractionOrigin.User)
        {
            return NativePoolInteractionRoute.NativeAction;
        }

        var wasSelected = _isSelected(State, id);
        var nextState = _setSelected(State, id, !wasSelected);
        var membershipChanged = _isSelected(nextState, id) != wasSelected;
        State = nextState;
        if (membershipChanged)
            _persist(nextState);

        return NativePoolInteractionRoute.PoolEdit;
    }

    public bool IsVisuallySelected(bool poolModeEnabled, string id, bool nativeSelected)
    {
        return poolModeEnabled ? _isSelected(State, id) : nativeSelected;
    }
}
