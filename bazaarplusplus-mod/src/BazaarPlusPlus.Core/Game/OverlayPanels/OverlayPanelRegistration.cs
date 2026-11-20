#nullable enable

namespace BazaarPlusPlus.Game.OverlayPanels;

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

    public Func<bool>? Enabled { get; init; }

    public Func<bool>? IsModalInputActive { get; init; }
    public Action? DismissModal { get; init; }

    // Scene cleanup runs after policy close even for hidden panels, which can still own runtime state.
    public Action? OnSceneChanged { get; init; }

    // Switch-off cleanup runs after the close and also reaches hidden panels.
    public Action? OnSwitchedOff { get; init; }

    // A false return must leave no partial view because the host will skip OnClose.
    public Func<bool> OnOpen { get; }

    public Action<OverlayCloseReason> OnClose { get; }

    // Hidden panels still tick so fades, warmup and deferred cleanup can finish.
    public Action<float, bool> Tick { get; }
}

public interface IOverlayPanelHandle : IDisposable
{
    bool IsVisible { get; }

    // External opens resolve synchronously so mutual exclusion holds within the calling frame.
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
