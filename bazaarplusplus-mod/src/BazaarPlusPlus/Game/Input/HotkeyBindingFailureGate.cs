#nullable enable
namespace BazaarPlusPlus.Game.Input;

internal sealed class HotkeyBindingFailureGate<TReason>
    where TReason : notnull
{
    private readonly HashSet<FailureKey> _failures = new();

    internal bool ShouldReport(
        BppHotkeyActionId actionId,
        string? bindingPath,
        TReason reasonCode
    ) => _failures.Add(new FailureKey(actionId, bindingPath ?? string.Empty, reasonCode));

    internal void Clear() => _failures.Clear();

    private readonly record struct FailureKey(
        BppHotkeyActionId ActionId,
        string BindingPath,
        TReason ReasonCode
    );
}
