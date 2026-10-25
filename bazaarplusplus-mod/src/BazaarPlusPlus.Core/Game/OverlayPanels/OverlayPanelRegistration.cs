#nullable enable

namespace BazaarPlusPlus.Game.OverlayPanels;

// What a Main Overlay Panel hands the Overlay Panel Host. Lifecycle (mutual exclusion, scene
// change, Panel Switch-Off, combat gate, toggle and escape routing, per-frame tick dispatch) is
// host-owned; the panel supplies content callbacks only.
public sealed class OverlayPanelRegistration
{
    public OverlayPanelRegistration(
        string panelId,
        Func<bool> onOpen,
        Action<OverlayCloseReason> onClose,
        Action<float, bool> tick
    )
    {
        PanelId = string.IsNullOrWhiteSpace(panelId)
            ? throw new ArgumentException("Panel id is required.", nameof(panelId))
            : panelId;
        OnOpen = onOpen ?? throw new ArgumentNullException(nameof(onOpen));
        OnClose = onClose ?? throw new ArgumentNullException(nameof(onClose));
        Tick = tick ?? throw new ArgumentNullException(nameof(tick));
    }

    public string PanelId { get; }

    public SceneChangeClosePolicy SceneChangeClose { get; init; } = SceneChangeClosePolicy.Always;

    // The panel's switch. Null means always enabled. A true-to-false edge closes the panel with
    // SwitchedOff and then calls OnSwitchedOff; a disabled panel never opens.
    public Func<bool>? Enabled { get; init; }

    public Func<bool>? IsModalInputActive { get; init; }
    public Action? DismissModal { get; init; }

    // Unconditional scene-change side effects (dispose runtime state, re-arm prewarm, ...).
    // Runs on every scene change regardless of visibility, after any policy-driven close.
    public Action? OnSceneChanged { get; init; }

    // Runs on every switch-off edge, open or not, after the SwitchedOff close.
    public Action? OnSwitchedOff { get; init; }

    // Returns false when the panel could not open; the panel stays closed and no OnClose follows.
    public Func<bool> OnOpen { get; }

    public Action<OverlayCloseReason> OnClose { get; }

    // Called every frame after lifecycle directives, with visibility read at call time. Panels
    // keep their closed-state work (fades, cache warmup, deferred cleanup) inside this.
    public Action<float, bool> Tick { get; }
}

public interface IOverlayPanelHandle : IDisposable
{
    bool IsVisible { get; }

    // Synchronous: enabled and combat gates, close-others and OnOpen run at the call site.
    OverlayRequestOutcome RequestOpen();

    OverlayRequestOutcome RequestClose();
}

public readonly record struct OverlayPanelFrame(
    string SceneToken,
    float DeltaTime,
    bool EscapePressed,
    string? ToggledPanelId
);

public enum SceneChangeClosePolicy
{
    Always,
    OnlyWhenInCombat,
}

public enum OverlayCloseReason
{
    SceneChange,
    Combat,
    HotkeyToggle,
    Superseded,
    Escape,
    Request,
    SwitchedOff,
    OpenFailed,
}

public enum OverlayRequestOutcome
{
    Executed,
    AlreadyInState,
    SuppressedByCombat,
    Disabled,
    OpenFailed,
    UnknownPanel,
    Busy,
}

// Why the Host's own resources (Android card rentals) are released. A plain close never
// releases them.
public enum OverlayReleaseReason
{
    SceneChange,
    SwitchedOff,
}

public enum OverlayEscapeKind
{
    None,
    DismissedModal,
    Closed,
}

public readonly record struct OverlayEscapeOutcome(OverlayEscapeKind Kind, string? PanelId)
{
    public bool Consumed => Kind != OverlayEscapeKind.None;
}
