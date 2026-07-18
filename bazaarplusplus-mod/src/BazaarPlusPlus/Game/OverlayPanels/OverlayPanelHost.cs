#nullable enable

using BazaarPlusPlus.Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BazaarPlusPlus.Game.OverlayPanels;

// The Overlay Panel Host (CONTEXT.md §Overlay panels): the single Unity adapter that reads
// frame inputs, runs the pure OverlayLifecycleCore rules, and dispatches open/close/tick
// callbacks to the registered Main Overlay Panels.
internal sealed class OverlayPanelHost : MonoBehaviour
{
    private readonly OverlayLifecycleCore _core = new();
    private readonly OverlayPanelHostLogState _logState = new();
    private readonly List<OverlayPanelRegistration> _registrations = new();

    public IOverlayPanelHandle Register(OverlayPanelRegistration registration)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));

        _core.RegisterPanel(registration.PanelId, registration.SceneChangeClose);
        _registrations.Add(registration);
        return new PanelHandle(this, registration.PanelId);
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        var snapshot = new OverlayFrameSnapshot
        {
            SceneToken = GetSceneToken(SceneManager.GetActiveScene()),
            IsInCombat = ReadIsInCombat(),
            EscapePressed = keyboard?.escapeKey.wasPressedThisFrame == true,
            HotkeyPressedPanelId = ResolvePressedHotkeyPanelId(keyboard),
        };

        ExecuteDirectives(_core.Evaluate(snapshot));

        var dt = Time.unscaledDeltaTime;
        var openPanelId = _core.OpenPanelId;
        foreach (var registration in _registrations)
        {
            _logState.ExecuteTick(
                registration.PanelId,
                () =>
                    registration.Tick(
                        dt,
                        string.Equals(registration.PanelId, openPanelId, StringComparison.Ordinal)
                    )
            );
        }
    }

    private string? ResolvePressedHotkeyPanelId(Keyboard? keyboard)
    {
        foreach (var registration in _registrations)
        {
            if (
                !BppHotkeyService.WasToggleHotkeyPressedThisFrame(
                    registration.ToggleHotkey,
                    keyboard
                )
            )
                continue;

            // A failing guard swallows the whole toggle (open and close) for this frame.
            return registration.HotkeyGuard?.Invoke() == false ? null : registration.PanelId;
        }

        return null;
    }

    private void ExecuteDirectives(IReadOnlyList<OverlayDirective> directives)
    {
        foreach (var directive in directives)
        {
            var registration = FindRegistration(directive.PanelId);
            if (registration == null)
                continue;

            _logState.ExecuteDirective(
                Guid.NewGuid(),
                directive.PanelId,
                directive.Kind,
                () =>
                {
                    switch (directive.Kind)
                    {
                        case OverlayDirectiveKind.Close:
                            registration.OnClose();
                            break;
                        case OverlayDirectiveKind.NotifySceneChanged:
                            registration.OnSceneChanged?.Invoke();
                            break;
                        case OverlayDirectiveKind.Open:
                            registration.OnOpen();
                            break;
                    }
                }
            );
        }
    }

    private OverlayPanelRegistration? FindRegistration(string panelId)
    {
        foreach (var registration in _registrations)
        {
            if (string.Equals(registration.PanelId, panelId, StringComparison.Ordinal))
                return registration;
        }

        return null;
    }

    private void Unregister(string panelId)
    {
        _core.UnregisterPanel(panelId);
        _logState.ForgetPanel(panelId);
        _registrations.RemoveAll(registration =>
            string.Equals(registration.PanelId, panelId, StringComparison.Ordinal)
        );
    }

    private bool ReadIsInCombat() =>
        _logState.ReadIsInCombat(static () => TheBazaar.Data.IsInCombat);

    private static string GetSceneToken(Scene scene) =>
        $"{scene.name}|{scene.path}|{scene.buildIndex}|{scene.isLoaded}";

    private sealed class PanelHandle : IOverlayPanelHandle
    {
        private readonly OverlayPanelHost _host;
        private readonly string _panelId;

        public PanelHandle(OverlayPanelHost host, string panelId)
        {
            _host = host;
            _panelId = panelId;
        }

        public bool IsVisible =>
            string.Equals(_host._core.OpenPanelId, _panelId, StringComparison.Ordinal);

        public OverlayRequestOutcome RequestOpen()
        {
            var outcome = _host._core.ExecuteOpenRequest(
                _panelId,
                _host.ReadIsInCombat(),
                out var directives
            );
            _host.ExecuteDirectives(directives);
            return outcome;
        }

        public OverlayRequestOutcome RequestClose()
        {
            var outcome = _host._core.ExecuteCloseRequest(_panelId, out var directives);
            _host.ExecuteDirectives(directives);
            return outcome;
        }

        public void Dispose() => _host.Unregister(_panelId);
    }
}
