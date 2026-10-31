#nullable enable

using BazaarPlusPlus.Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BazaarPlusPlus.Game.OverlayPanels;

// The desktop adapter of the Overlay Panel Host (GLOSSARY.md §Overlay panels): it reads the
// frame's scene, Escape and toggle hotkeys, and feeds them to the Core OverlayPanelHost, which
// owns the lifecycle of the three Main Overlay Panels.
internal sealed class DesktopOverlayPanels : MonoBehaviour
{
    private readonly OverlayPanelHost _host = new(static () => TheBazaar.Data.IsInCombat);
    private readonly List<(string PanelId, BppHotkeyActionId Hotkey, Func<bool>? Guard)> _hotkeys =
        new();
    private Scene _sceneTokenScene;
    private bool _sceneTokenLoaded;
    private string _sceneToken = string.Empty;

    // hotkeyGuard returning false swallows the whole toggle (open and close) for that frame,
    // e.g. while a text input inside the panel has focus. A null toggleHotkey registers a panel
    // that no key toggles, as the Mobile UI's panels (ADR-0024).
    public IOverlayPanelHandle Register(
        OverlayPanelRegistration registration,
        BppHotkeyActionId? toggleHotkey,
        Func<bool>? hotkeyGuard = null
    )
    {
        var handle = _host.Register(registration);
        if (toggleHotkey is { } hotkey)
            _hotkeys.Add((registration.PanelId, hotkey, hotkeyGuard));
        return new HotkeyHandle(this, registration.PanelId, handle);
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        _host.Frame(
            new OverlayPanelFrame(
                GetSceneToken(SceneManager.GetActiveScene()),
                Time.unscaledDeltaTime,
                keyboard?.escapeKey.wasPressedThisFrame == true,
                ResolvePressedHotkeyPanelId(keyboard)
            )
        );
    }

    private string? ResolvePressedHotkeyPanelId(Keyboard? keyboard)
    {
        foreach (var (panelId, hotkey, guard) in _hotkeys)
        {
            if (!BppHotkeyService.WasToggleHotkeyPressedThisFrame(hotkey, keyboard))
                continue;

            // A failing guard swallows the whole toggle (open and close) for this frame.
            return guard?.Invoke() == false ? null : panelId;
        }

        return null;
    }

    // Compare Scene values, not Scene.handle: Unity 6 changed that getter's return type
    // from int to SceneHandle. The equality operator's ABI is stable across both versions.
    private string GetSceneToken(Scene scene)
    {
        if (
            _sceneToken.Length != 0
            && _sceneTokenScene == scene
            && _sceneTokenLoaded == scene.isLoaded
        )
            return _sceneToken;

        _sceneTokenScene = scene;
        _sceneTokenLoaded = scene.isLoaded;
        _sceneToken = $"{scene.name}|{scene.path}|{scene.buildIndex}|{scene.isLoaded}";
        return _sceneToken;
    }

    private sealed class HotkeyHandle : IOverlayPanelHandle
    {
        private readonly DesktopOverlayPanels _owner;
        private readonly string _panelId;
        private readonly IOverlayPanelHandle _inner;

        public HotkeyHandle(DesktopOverlayPanels owner, string panelId, IOverlayPanelHandle inner)
        {
            _owner = owner;
            _panelId = panelId;
            _inner = inner;
        }

        public bool IsVisible => _inner.IsVisible;

        public OverlayRequestOutcome RequestOpen() => _inner.RequestOpen();

        public OverlayRequestOutcome RequestClose() => _inner.RequestClose();

        public void Dispose()
        {
            _inner.Dispose();
            _owner._hotkeys.RemoveAll(hotkey =>
                string.Equals(hotkey.PanelId, _panelId, StringComparison.Ordinal)
            );
        }
    }
}
