#nullable enable

namespace BazaarPlusPlus.Game.OverlayPanels;

internal enum OverlayDirectiveKind
{
    Close,
    NotifySceneChanged,
    Open,
    DismissModal,
    NotifySwitchedOff,
    Release,
}

internal readonly struct OverlayDirective
{
    public OverlayDirectiveKind Kind { get; init; }

    public string PanelId { get; init; }
    public OverlayCloseReason CloseReason { get; init; }
    public OverlayReleaseReason ReleaseReason { get; init; }

    public static OverlayDirective Close(string panelId, OverlayCloseReason reason) =>
        new()
        {
            Kind = OverlayDirectiveKind.Close,
            PanelId = panelId,
            CloseReason = reason,
        };

    public static OverlayDirective NotifySceneChanged(string panelId) =>
        new() { Kind = OverlayDirectiveKind.NotifySceneChanged, PanelId = panelId };

    public static OverlayDirective NotifySwitchedOff(string panelId) =>
        new() { Kind = OverlayDirectiveKind.NotifySwitchedOff, PanelId = panelId };

    public static OverlayDirective Open(string panelId) =>
        new() { Kind = OverlayDirectiveKind.Open, PanelId = panelId };

    public static OverlayDirective DismissModal(string panelId) =>
        new() { Kind = OverlayDirectiveKind.DismissModal, PanelId = panelId };

    public static OverlayDirective Release(OverlayReleaseReason reason) =>
        new()
        {
            Kind = OverlayDirectiveKind.Release,
            PanelId = string.Empty,
            ReleaseReason = reason,
        };
}

internal readonly struct OverlayFrameSnapshot
{
    public string? SceneToken { get; init; }
    public bool IsInCombat { get; init; }
    public IReadOnlyList<string>? SwitchedOff { get; init; }
    public bool EscapePressed { get; init; }
    public string? ToggledPanelId { get; init; }
    public bool ToggledPanelEnabled { get; init; }
    public bool IsModalInputActive { get; init; }
    public bool ObservesRules { get; init; }
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

    public bool IsRegistered(string panelId) => FindPanel(panelId) != null;

    public IReadOnlyList<OverlayDirective> Evaluate(OverlayFrameSnapshot frame)
    {
        List<OverlayDirective>? directives = null;
        OverlayReleaseReason? release = null;

        if (frame.SceneToken is { } sceneToken)
        {
            if (_lastSceneToken == null)
            {
                _lastSceneToken = sceneToken;
            }
            else if (!string.Equals(sceneToken, _lastSceneToken, StringComparison.Ordinal))
            {
                _lastSceneToken = sceneToken;
                if (_openPanelId != null)
                {
                    var policy =
                        FindPanel(_openPanelId)?.ScenePolicy ?? SceneChangeClosePolicy.Always;
                    if (policy == SceneChangeClosePolicy.Always || frame.IsInCombat)
                        ClosePanel(ref directives, OverlayCloseReason.SceneChange);
                }

                // Close before disposing so panels can release visible state first.
                foreach (var panel in _panels)
                    (directives ??= []).Add(OverlayDirective.NotifySceneChanged(panel.PanelId));
                release = OverlayReleaseReason.SceneChange;
            }
        }

        if (frame.SwitchedOff is { Count: > 0 } switchedOff)
        {
            foreach (var panelId in switchedOff)
            {
                if (FindPanel(panelId) == null)
                    continue;
                if (string.Equals(_openPanelId, panelId, StringComparison.Ordinal))
                    ClosePanel(ref directives, OverlayCloseReason.SwitchedOff);
                (directives ??= []).Add(OverlayDirective.NotifySwitchedOff(panelId));
                release ??= OverlayReleaseReason.SwitchedOff;
            }
        }

        // The Host's resources go once, after every panel let go of its own.
        if (release is { } releaseReason)
            (directives ??= []).Add(OverlayDirective.Release(releaseReason));

        if (!frame.ObservesRules)
            return (IReadOnlyList<OverlayDirective>?)directives ?? Array.Empty<OverlayDirective>();

        if (_openPanelId != null && frame.IsInCombat)
            ClosePanel(ref directives, OverlayCloseReason.Combat);

        if (frame.IsModalInputActive)
        {
            if (_openPanelId != null && frame.EscapePressed)
                (directives ??= []).Add(OverlayDirective.DismissModal(_openPanelId));
            return (IReadOnlyList<OverlayDirective>?)directives ?? Array.Empty<OverlayDirective>();
        }

        if (frame.ToggledPanelId is { } toggledPanelId && FindPanel(toggledPanelId) != null)
        {
            if (string.Equals(_openPanelId, toggledPanelId, StringComparison.Ordinal))
            {
                ClosePanel(ref directives, OverlayCloseReason.HotkeyToggle);
            }
            else if (frame.ToggledPanelEnabled && !frame.IsInCombat)
            {
                if (_openPanelId != null)
                    ClosePanel(ref directives, OverlayCloseReason.Superseded);
                (directives ??= []).Add(OverlayDirective.Open(toggledPanelId));
                _openPanelId = toggledPanelId;
            }
        }
        else if (frame.EscapePressed && _openPanelId != null)
        {
            ClosePanel(ref directives, OverlayCloseReason.Escape);
        }

        return (IReadOnlyList<OverlayDirective>?)directives ?? Array.Empty<OverlayDirective>();
    }

    // Android's Harmony back-key prefix needs the Escape decision synchronously.
    public IReadOnlyList<OverlayDirective> EvaluateEscape(bool isModalInputActive)
    {
        if (_openPanelId == null)
            return Array.Empty<OverlayDirective>();
        if (isModalInputActive)
            return [OverlayDirective.DismissModal(_openPanelId)];
        List<OverlayDirective>? directives = null;
        ClosePanel(ref directives, OverlayCloseReason.Escape);
        return directives!;
    }

    // Dock and menu opens resolve synchronously so mutual exclusion holds in the same frame.
    public OverlayRequestOutcome ExecuteOpenRequest(
        string panelId,
        bool isEnabled,
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
        if (!isEnabled)
            return OverlayRequestOutcome.Disabled;
        if (isInCombat)
            return OverlayRequestOutcome.SuppressedByCombat;

        List<OverlayDirective>? pending = result;
        if (_openPanelId != null)
            ClosePanel(ref pending, OverlayCloseReason.Superseded);
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

        List<OverlayDirective>? pending = result;
        ClosePanel(ref pending, OverlayCloseReason.Request);
        return OverlayRequestOutcome.Executed;
    }

    public void RollbackOpen(string panelId)
    {
        if (string.Equals(_openPanelId, panelId, StringComparison.Ordinal))
            _openPanelId = null;
    }

    private void ClosePanel(ref List<OverlayDirective>? directives, OverlayCloseReason reason)
    {
        (directives ??= []).Add(OverlayDirective.Close(_openPanelId!, reason));
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
