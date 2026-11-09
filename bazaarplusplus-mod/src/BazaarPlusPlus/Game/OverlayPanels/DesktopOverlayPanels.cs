#nullable enable

using BazaarPlusPlus.Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BazaarPlusPlus.Game.OverlayPanels;

// The desktop adapter of the Overlay Panel Host (GLOSSARY.md §Overlay panels): it reads the
// frame's scene, Escape, the toggle hotkeys and the Walkthrough Control, and feeds them to the
// Core OverlayPanelHost, which owns the lifecycle of the three Main Overlay Panels.
internal sealed class DesktopOverlayPanels : MonoBehaviour
{
    private readonly OverlayPanelHost _host = new(static () => TheBazaar.Data.IsInCombat);
    private readonly List<(string PanelId, BppHotkeyActionId Hotkey, Func<bool>? Guard)> _hotkeys =
        new();
    private Scene _sceneTokenScene;
    private bool _sceneTokenLoaded;
    private string _sceneToken = string.Empty;

    // The open Main Overlay Panel's id, or null.
    public string? OpenPanelId => _host.OpenPanelId;

    // Set by the mount when the host has a Data Root; null leaves only the hotkeys.
    public WalkthroughControl? Walkthrough { get; set; }

    // Where a Walkthrough Control `shot` writes its capture; set with Walkthrough.
    public string? ShotDirectory { get; set; }

    // A panel the Walkthrough Control alone opens, such as the Debug-build Kit Gallery.
    public IOverlayPanelHandle Register(OverlayPanelRegistration registration) =>
        _host.Register(registration);

    // hotkeyGuard returning false swallows the whole toggle (open and close) for that frame,
    // e.g. while a text input inside the panel has focus.
    public IOverlayPanelHandle Register(
        OverlayPanelRegistration registration,
        BppHotkeyActionId toggleHotkey,
        Func<bool>? hotkeyGuard = null
    )
    {
        var handle = _host.Register(registration);
        _hotkeys.Add((registration.PanelId, toggleHotkey, hotkeyGuard));
        return new HotkeyHandle(this, registration.PanelId, handle);
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        var deltaTime = Time.unscaledDeltaTime;
        var toggle =
            ResolvePressedHotkeyPanelId(keyboard)
            ?? Walkthrough?.NextToggle(deltaTime, _host.OpenPanelId);
        _host.Frame(
            new OverlayPanelFrame(
                GetSceneToken(SceneManager.GetActiveScene()),
                deltaTime,
                keyboard?.escapeKey.wasPressedThisFrame == true,
                toggle
            )
        );
        Walkthrough?.AfterFrame(_host.OpenPanelId);
        if (Walkthrough?.TakeShot() is { } shot && ShotDirectory != null)
            StartCoroutine(DesktopUiShot.Capture(ShotDirectory, shot));
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
