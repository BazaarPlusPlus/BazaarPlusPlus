#nullable enable
using TheBazaar.AppFramework;
using TheBazaar.Inputs;

namespace BazaarPlusPlus.GameInterop.Input;

// Suppression belongs to an input manager, not a duplicate enum entry in its context stack.
internal sealed class NativeTextInputLease : IDisposable
{
    private static readonly Dictionary<InputContextStack, int> Owners = new();
    private InputManager? _manager;
    private readonly InputContextStack _context;

    private NativeTextInputLease(InputManager manager)
    {
        _manager = manager;
        _context = manager.Context;
        Owners.TryGetValue(_context, out var count);
        Owners[_context] = count + 1;
        Reapply(_context);
    }

    internal static bool IsActive => Owners.Count != 0;
    internal bool IsCurrent =>
        _manager != null
        && Services.TryGet<InputManager>(out var current)
        && current == _manager
        && ReferenceEquals(current.Context, _context);

    internal static NativeTextInputLease? TryAcquire() =>
        Services.TryGet<InputManager>(out var manager)
        && manager != null
        && manager.IsReady
        && manager.Context != null
            ? new NativeTextInputLease(manager)
            : null;

    internal static void Reapply(InputContextStack context)
    {
        if (!Owners.ContainsKey(context))
            return;
        context._actions.Gameplay.Disable();
        context._actions.MenuShortcuts.Disable();
    }

    public void Dispose()
    {
        if (ReferenceEquals(_manager, null))
            return;
        var manager = _manager;
        _manager = null;
        var remaining = Owners[_context] - 1;
        if (remaining > 0)
            Owners[_context] = remaining;
        else
            Owners.Remove(_context);
        // Restore the context that is active now; it may have changed while typing.
        if (manager != null && manager.IsReady && ReferenceEquals(manager.Context, _context))
            _context.Apply();
    }
}
