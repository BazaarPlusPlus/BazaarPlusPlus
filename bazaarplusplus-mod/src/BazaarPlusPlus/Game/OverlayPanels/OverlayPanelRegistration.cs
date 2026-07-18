#nullable enable

using BazaarPlusPlus.Game.Input;

namespace BazaarPlusPlus.Game.OverlayPanels;

// What a Main Overlay Panel hands the Overlay Panel Host. Lifecycle (mutual exclusion, scene
// change, combat gate, hotkey/escape routing, per-frame tick dispatch) is host-owned; the
// panel supplies content callbacks only.
internal sealed class OverlayPanelRegistration
{
    public OverlayPanelRegistration(
        string panelId,
        BppHotkeyActionId toggleHotkey,
        Action onOpen,
        Action onClose,
        Action<float, bool> tick
    )
    {
        PanelId = string.IsNullOrWhiteSpace(panelId)
            ? throw new ArgumentException("Panel id is required.", nameof(panelId))
            : panelId;
        ToggleHotkey = toggleHotkey;
        OnOpen = onOpen ?? throw new ArgumentNullException(nameof(onOpen));
        OnClose = onClose ?? throw new ArgumentNullException(nameof(onClose));
        Tick = tick ?? throw new ArgumentNullException(nameof(tick));
    }

    public string PanelId { get; }

    public BppHotkeyActionId ToggleHotkey { get; }

    public SceneChangeClosePolicy SceneChangeClose { get; init; } = SceneChangeClosePolicy.Always;

    // Return false to swallow the whole hotkey toggle for this frame (open AND close), e.g.
    // while a text input inside the panel has focus.
    public Func<bool>? HotkeyGuard { get; init; }

    // Unconditional scene-change side effects (dispose Unity runtime, re-arm prewarm, ...).
    // Runs on every scene change regardless of visibility, after any policy-driven close.
    public Action? OnSceneChanged { get; init; }

    public Action OnOpen { get; }

    public Action OnClose { get; }

    // Called every frame after lifecycle directives, with post-directive visibility. Panels
    // keep their closed-state work (fades, cache warmup, deferred cleanup) inside this.
    public Action<float, bool> Tick { get; }
}

internal interface IOverlayPanelHandle
{
    bool IsVisible { get; }

    // Synchronous: combat gate + close-others + OnOpen execute at the call site.
    OverlayRequestOutcome RequestOpen();

    OverlayRequestOutcome RequestClose();

    void Dispose();
}
