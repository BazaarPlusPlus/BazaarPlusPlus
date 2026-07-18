#nullable enable

namespace BazaarPlusPlus.Game.OverlayPanels;

// Pure lifecycle rules for Main Overlay Panels (CONTEXT.md §Overlay panels). This file must
// stay free of Unity/game references: it is compile-linked into OverlayPanelLifecycle.Tests.

internal enum SceneChangeClosePolicy
{
    Always,
    OnlyWhenInCombat,
}

internal enum OverlayDirectiveKind
{
    Close,
    NotifySceneChanged,
    Open,
}

internal enum OverlayCloseReason
{
    None,
    SceneChange,
    Combat,
    HotkeyToggle,
    Superseded,
    Escape,
    Request,
}

internal readonly struct OverlayDirective
{
    public OverlayDirectiveKind Kind { get; init; }
    public string PanelId { get; init; }
    public OverlayCloseReason CloseReason { get; init; }

    public static OverlayDirective Close(string panelId, OverlayCloseReason reason) =>
        new()
        {
            Kind = OverlayDirectiveKind.Close,
            PanelId = panelId,
            CloseReason = reason,
        };

    public static OverlayDirective NotifySceneChanged(string panelId) =>
        new() { Kind = OverlayDirectiveKind.NotifySceneChanged, PanelId = panelId };

    public static OverlayDirective Open(string panelId) =>
        new() { Kind = OverlayDirectiveKind.Open, PanelId = panelId };
}

// One frame of adapter-read inputs. HotkeyPressedPanelId is resolved by the adapter with the
// panel's hotkey guard already applied (a guarded hotkey never reaches the snapshot).
internal readonly struct OverlayFrameSnapshot
{
    public string SceneToken { get; init; }
    public bool IsInCombat { get; init; }
    public bool EscapePressed { get; init; }
    public string? HotkeyPressedPanelId { get; init; }
}

internal enum OverlayRequestOutcome
{
    Executed,
    SuppressedByCombat,
    AlreadyInState,
    UnknownPanel,
}

internal sealed class OverlayLifecycleCore
{
    private readonly List<(string PanelId, SceneChangeClosePolicy ScenePolicy)> _panels = new();
    private string? _openPanelId;
    private string? _lastSceneToken;

    public string? OpenPanelId => _openPanelId;

    public void RegisterPanel(string panelId, SceneChangeClosePolicy scenePolicy)
    {
        if (string.IsNullOrWhiteSpace(panelId))
            throw new ArgumentException("Panel id is required.", nameof(panelId));
        if (FindPanel(panelId) != null)
            throw new InvalidOperationException($"Panel '{panelId}' is already registered.");

        _panels.Add((panelId, scenePolicy));
    }

    public void UnregisterPanel(string panelId)
    {
        _panels.RemoveAll(panel => string.Equals(panel.PanelId, panelId, StringComparison.Ordinal));
        if (string.Equals(_openPanelId, panelId, StringComparison.Ordinal))
            _openPanelId = null;
    }

    // Rule order matches the panels' historical per-frame Update order:
    // scene change -> combat auto-close -> hotkey toggle -> escape.
    public IReadOnlyList<OverlayDirective> Evaluate(OverlayFrameSnapshot frame)
    {
        var directives = new List<OverlayDirective>();

        if (_lastSceneToken == null)
        {
            _lastSceneToken = frame.SceneToken;
        }
        else if (!string.Equals(frame.SceneToken, _lastSceneToken, StringComparison.Ordinal))
        {
            _lastSceneToken = frame.SceneToken;
            if (_openPanelId != null)
            {
                var policy = FindPanel(_openPanelId)?.ScenePolicy ?? SceneChangeClosePolicy.Always;
                if (policy == SceneChangeClosePolicy.Always || frame.IsInCombat)
                    ClosePanel(directives, OverlayCloseReason.SceneChange);
            }

            // Side-effect fan-out runs after the policy close (matching CollectionPanel's
            // close-then-dispose order) and reaches every registrant, open or closed.
            foreach (var panel in _panels)
                directives.Add(OverlayDirective.NotifySceneChanged(panel.PanelId));
        }

        if (_openPanelId != null && frame.IsInCombat)
            ClosePanel(directives, OverlayCloseReason.Combat);

        if (frame.HotkeyPressedPanelId is { } hotkeyPanelId && FindPanel(hotkeyPanelId) != null)
        {
            if (string.Equals(_openPanelId, hotkeyPanelId, StringComparison.Ordinal))
            {
                ClosePanel(directives, OverlayCloseReason.HotkeyToggle);
            }
            else if (!frame.IsInCombat)
            {
                if (_openPanelId != null)
                    ClosePanel(directives, OverlayCloseReason.Superseded);
                directives.Add(OverlayDirective.Open(hotkeyPanelId));
                _openPanelId = hotkeyPanelId;
            }
            // In combat: open is suppressed, matching the panels' historical Open() guards.
        }
        else if (frame.EscapePressed && _openPanelId != null)
        {
            ClosePanel(directives, OverlayCloseReason.Escape);
        }

        return directives;
    }

    // Synchronous external open (dock entries): combat gate + close-others + open, resolved at
    // the call site so mutual exclusion stays same-frame like the historical dock click path.
    public OverlayRequestOutcome ExecuteOpenRequest(
        string panelId,
        bool isInCombat,
        out IReadOnlyList<OverlayDirective> directives
    )
    {
        var result = new List<OverlayDirective>();
        directives = result;

        if (FindPanel(panelId) == null)
            return OverlayRequestOutcome.UnknownPanel;
        if (string.Equals(_openPanelId, panelId, StringComparison.Ordinal))
            return OverlayRequestOutcome.AlreadyInState;
        if (isInCombat)
            return OverlayRequestOutcome.SuppressedByCombat;

        if (_openPanelId != null)
            ClosePanel(result, OverlayCloseReason.Superseded);
        result.Add(OverlayDirective.Open(panelId));
        _openPanelId = panelId;
        return OverlayRequestOutcome.Executed;
    }

    public OverlayRequestOutcome ExecuteCloseRequest(
        string panelId,
        out IReadOnlyList<OverlayDirective> directives
    )
    {
        var result = new List<OverlayDirective>();
        directives = result;

        if (FindPanel(panelId) == null)
            return OverlayRequestOutcome.UnknownPanel;
        if (!string.Equals(_openPanelId, panelId, StringComparison.Ordinal))
            return OverlayRequestOutcome.AlreadyInState;

        ClosePanel(result, OverlayCloseReason.Request);
        return OverlayRequestOutcome.Executed;
    }

    private void ClosePanel(List<OverlayDirective> directives, OverlayCloseReason reason)
    {
        directives.Add(OverlayDirective.Close(_openPanelId!, reason));
        _openPanelId = null;
    }

    private (string PanelId, SceneChangeClosePolicy ScenePolicy)? FindPanel(string panelId)
    {
        foreach (var panel in _panels)
        {
            if (string.Equals(panel.PanelId, panelId, StringComparison.Ordinal))
                return panel;
        }

        return null;
    }
}
