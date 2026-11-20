#nullable enable

using BazaarPlusPlus.Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BazaarPlusPlus.Game.OverlayPanels;

internal sealed class DesktopOverlayPanels : MonoBehaviour
{
    private readonly OverlayPanelHost _host = new(static () => TheBazaar.Data.IsInCombat);
    private readonly List<(string PanelId, BppHotkeyActionId Hotkey, Func<bool>? Guard)> _hotkeys =
        new();
    private Scene _sceneTokenScene;
    private bool _sceneTokenLoaded;
    private string _sceneToken = string.Empty;

    public string? OpenPanelId => _host.OpenPanelId;

    public WalkthroughControl? Walkthrough { get; set; }

    public string? ShotDirectory { get; set; }

    public IOverlayPanelHandle Register(OverlayPanelRegistration registration) =>
        _host.Register(registration);

    // The guard blocks both opening and closing, including while panel text input has focus.
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

            return guard?.Invoke() == false ? null : panelId;
        }

        return null;
    }

    // Unity 6 changed Scene.handle from int to SceneHandle. Scene equality has a stable ABI across both versions.
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
