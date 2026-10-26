#nullable enable

namespace BazaarPlusPlus.Game.OverlayPanels;

// The Overlay Panel Host (GLOSSARY.md §Overlay panels): it holds the registered Main Overlay
// Panels, reads each evaluation's facts from them, asks the pure OverlayLifecycleCore for
// directives, and runs those directives with every callback guarded on its own. One adapter
// per Host feeds it frames: DesktopOverlayPanels and AndroidOverlayPanels.
public sealed class OverlayPanelHost
{
    private const string HostDirectiveTarget = "host";

    private readonly OverlayLifecycleCore _core = new();
    private readonly OverlayPanelHostLogState _logState = new();
    private readonly Func<bool> _isInCombat;
    private readonly Action<OverlayReleaseReason>? _releaseHostResources;
    private readonly List<Entry> _entries = new();
    private Entry[] _tickOrder = Array.Empty<Entry>();
    private int _lifecycleCallbackDepth;

    // isInCombat is read lazily: only when a panel is open, or a toggle or open is evaluated.
    public OverlayPanelHost(
        Func<bool> isInCombat,
        Action<OverlayReleaseReason>? releaseHostResources = null
    )
    {
        _isInCombat = isInCombat ?? throw new ArgumentNullException(nameof(isInCombat));
        _releaseHostResources = releaseHostResources;
    }

    public string? OpenPanelId => _core.OpenPanelId;

    public IOverlayPanelHandle Register(OverlayPanelRegistration registration)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));

        _core.RegisterPanel(registration.PanelId, registration.SceneChangeClose);
        var entry = new Entry(registration);
        // The first value is adopted here, so a switch-off before the next frame is an edge.
        ReadEnabled(entry, recordEdge: true);
        _entries.Add(entry);
        _tickOrder = _entries.ToArray();
        return new PanelHandle(this, entry);
    }

    // Once per frame: the lifecycle rules, then every registrant's tick.
    public void Frame(OverlayPanelFrame frame)
    {
        if (frame.SceneToken == null)
            throw new ArgumentException("A scene token is required.", nameof(frame));

        var openEntry = FindEntry(_core.OpenPanelId);
        var modalActive =
            openEntry?.Registration.IsModalInputActive is { } isModal
            && _logState.ReadPredicate(
                openEntry.Registration.PanelId,
                OverlayPredicateKind.IsModalInputActive,
                isModal,
                fallback: false
            );
        var switchedOff = ReadSwitchOffEdges();
        var toggled = frame.ToggledPanelId == null ? null : FindEntry(frame.ToggledPanelId);
        var toggledEnabled = toggled != null && toggled.LastEnabled != false;
        var snapshot = new OverlayFrameSnapshot
        {
            SceneToken = frame.SceneToken,
            IsInCombat = (openEntry != null || toggled != null) && ReadIsInCombat(),
            SwitchedOff = switchedOff,
            EscapePressed = frame.EscapePressed,
            ToggledPanelId = toggled?.Registration.PanelId,
            ToggledPanelEnabled = toggledEnabled,
            IsModalInputActive = modalActive,
            ObservesRules = true,
        };

        ExecuteDirectives(_core.Evaluate(snapshot));

        var dt = frame.DeltaTime;
        foreach (var entry in _tickOrder)
        {
            if (entry.Unregistered)
                continue;
            _logState.ExecuteTick(
                entry.Registration.PanelId,
                entry.Registration.Tick,
                dt,
                IsOpen(entry)
            );
        }
    }

    // Synchronous scene observation: the scene, switch-off and release rules, no ticks. The
    // same token is a no-op; the first token ever seen is adopted silently.
    public void ObserveScene(string sceneToken)
    {
        if (sceneToken == null)
            throw new ArgumentNullException(nameof(sceneToken));

        var snapshot = new OverlayFrameSnapshot
        {
            SceneToken = sceneToken,
            IsInCombat = _core.OpenPanelId != null && ReadIsInCombat(),
            SwitchedOff = ReadSwitchOffEdges(),
            ObservesRules = false,
        };
        ExecuteDirectives(_core.Evaluate(snapshot));
    }

    // Synchronous Escape (the Android back key, answered inside Harmony prefixes): the modal
    // rule, then the escape close. Consumed when a panel was open.
    public OverlayEscapeOutcome Escape()
    {
        var openEntry = FindEntry(_core.OpenPanelId);
        if (openEntry == null)
            return new OverlayEscapeOutcome(OverlayEscapeKind.None, null);

        var panelId = openEntry.Registration.PanelId;
        if (_lifecycleCallbackDepth > 0)
        {
            OverlayPanelHostLogState.ReportReentrantRequest(
                panelId,
                OverlayPanelRequestKind.Escape
            );
            return new OverlayEscapeOutcome(OverlayEscapeKind.None, null);
        }

        var modalActive =
            openEntry.Registration.IsModalInputActive is { } isModal
            && _logState.ReadPredicate(
                panelId,
                OverlayPredicateKind.IsModalInputActive,
                isModal,
                fallback: false
            );
        ExecuteDirectives(_core.EvaluateEscape(modalActive));
        return new OverlayEscapeOutcome(
            modalActive ? OverlayEscapeKind.DismissedModal : OverlayEscapeKind.Closed,
            panelId
        );
    }

    private OverlayRequestOutcome RequestOpen(Entry entry)
    {
        var panelId = entry.Registration.PanelId;
        if (entry.Unregistered || !_core.IsRegistered(panelId))
            return OverlayRequestOutcome.UnknownPanel;
        if (IsOpen(entry))
            return OverlayRequestOutcome.AlreadyInState;
        if (_lifecycleCallbackDepth > 0)
        {
            OverlayPanelHostLogState.ReportReentrantRequest(panelId, OverlayPanelRequestKind.Open);
            return OverlayRequestOutcome.Busy;
        }

        // Not recorded as an edge: the next evaluation still sees a switch-off it missed.
        var enabled = ReadEnabled(entry, recordEdge: false);
        var outcome = _core.ExecuteOpenRequest(
            panelId,
            enabled,
            enabled && ReadIsInCombat(),
            out var directives
        );
        if (outcome != OverlayRequestOutcome.Executed)
            return outcome;

        return ExecuteDirectives(directives)
            ? OverlayRequestOutcome.Executed
            : OverlayRequestOutcome.OpenFailed;
    }

    private OverlayRequestOutcome RequestClose(Entry entry)
    {
        var panelId = entry.Registration.PanelId;
        if (entry.Unregistered || !_core.IsRegistered(panelId))
            return OverlayRequestOutcome.UnknownPanel;
        if (!IsOpen(entry))
            return OverlayRequestOutcome.AlreadyInState;
        if (_lifecycleCallbackDepth > 0)
        {
            OverlayPanelHostLogState.ReportReentrantRequest(panelId, OverlayPanelRequestKind.Close);
            return OverlayRequestOutcome.Busy;
        }

        _core.ExecuteCloseRequest(panelId, out var directives);
        ExecuteDirectives(directives);
        return OverlayRequestOutcome.Executed;
    }

    private void Unregister(Entry entry)
    {
        if (entry.Unregistered)
            return;

        entry.Unregistered = true;
        var panelId = entry.Registration.PanelId;
        _core.UnregisterPanel(panelId);
        _logState.ForgetPanel(panelId);
        _entries.Remove(entry);
        _tickOrder = _entries.ToArray();
    }

    // Reads every switchable panel's Enabled predicate and returns the panels whose value went
    // from true to false since the last read; Register adopted each panel's first value.
    private List<string>? ReadSwitchOffEdges()
    {
        List<string>? switchedOff = null;
        foreach (var entry in _tickOrder)
        {
            if (entry.Unregistered || entry.Registration.Enabled == null)
                continue;
            var previous = entry.LastEnabled;
            var current = ReadEnabled(entry, recordEdge: true);
            if (previous == true && !current)
                (switchedOff ??= []).Add(entry.Registration.PanelId);
        }

        return switchedOff;
    }

    private bool ReadEnabled(Entry entry, bool recordEdge)
    {
        if (entry.Registration.Enabled is not { } enabled)
            return true;

        // A failing predicate keeps the last value, so a throw never reads as a switch-off.
        var current = _logState.ReadPredicate(
            entry.Registration.PanelId,
            OverlayPredicateKind.Enabled,
            enabled,
            fallback: entry.LastEnabled ?? true
        );
        if (recordEdge)
            entry.LastEnabled = current;
        return current;
    }

    private bool ReadIsInCombat() => _logState.ReadIsInCombat(_isInCombat);

    // Returns false when an Open directive did not open its panel.
    private bool ExecuteDirectives(IReadOnlyList<OverlayDirective> directives)
    {
        var opened = true;
        foreach (var directive in directives)
        {
            if (directive.Kind == OverlayDirectiveKind.Release)
            {
                if (_releaseHostResources is { } release)
                {
                    var reason = directive.ReleaseReason;
                    RunLifecycleCallback(
                        HostDirectiveTarget,
                        directive.Kind,
                        () => release(reason)
                    );
                }
                continue;
            }

            var entry = FindEntry(directive.PanelId);
            if (entry == null)
                continue;

            var registration = entry.Registration;
            switch (directive.Kind)
            {
                case OverlayDirectiveKind.Close:
                    var closeReason = directive.CloseReason;
                    RunLifecycleCallback(
                        registration.PanelId,
                        directive.Kind,
                        () => registration.OnClose(closeReason)
                    );
                    break;
                case OverlayDirectiveKind.Open:
                    opened = Open(entry) && opened;
                    break;
                case OverlayDirectiveKind.NotifySceneChanged:
                    if (registration.OnSceneChanged is { } onSceneChanged)
                        RunLifecycleCallback(registration.PanelId, directive.Kind, onSceneChanged);
                    break;
                case OverlayDirectiveKind.NotifySwitchedOff:
                    if (registration.OnSwitchedOff is { } onSwitchedOff)
                        RunLifecycleCallback(registration.PanelId, directive.Kind, onSwitchedOff);
                    break;
                case OverlayDirectiveKind.DismissModal:
                    if (registration.DismissModal is { } dismissModal)
                        RunLifecycleCallback(registration.PanelId, directive.Kind, dismissModal);
                    break;
            }
        }

        return opened;
    }

    private bool Open(Entry entry)
    {
        var registration = entry.Registration;
        var opened = false;
        var completed = RunLifecycleCallback(
            registration.PanelId,
            OverlayDirectiveKind.Open,
            () => opened = registration.OnOpen()
        );
        if (completed && opened)
            return true;

        _core.RollbackOpen(registration.PanelId);
        // A throw may have left partial state (rented cards, a half-built view): the panel
        // gets a close so it can return it. A clean false opened nothing, so no close follows.
        if (!completed)
        {
            RunLifecycleCallback(
                registration.PanelId,
                OverlayDirectiveKind.Close,
                () => registration.OnClose(OverlayCloseReason.OpenFailed)
            );
        }

        return false;
    }

    private bool RunLifecycleCallback(string panelId, OverlayDirectiveKind kind, Action callback)
    {
        _lifecycleCallbackDepth++;
        try
        {
            return _logState.ExecuteDirective(Guid.NewGuid(), panelId, kind, callback);
        }
        finally
        {
            _lifecycleCallbackDepth--;
        }
    }

    private bool IsOpen(Entry entry) =>
        !entry.Unregistered
        && string.Equals(_core.OpenPanelId, entry.Registration.PanelId, StringComparison.Ordinal);

    private Entry? FindEntry(string? panelId)
    {
        if (panelId == null)
            return null;
        foreach (var entry in _entries)
        {
            if (string.Equals(entry.Registration.PanelId, panelId, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    private sealed class Entry
    {
        public Entry(OverlayPanelRegistration registration) => Registration = registration;

        public OverlayPanelRegistration Registration { get; }

        // Null until the first read: the first value is adopted silently.
        public bool? LastEnabled { get; set; }

        public bool Unregistered { get; set; }
    }

    private sealed class PanelHandle : IOverlayPanelHandle
    {
        private readonly OverlayPanelHost _host;
        private readonly Entry _entry;

        public PanelHandle(OverlayPanelHost host, Entry entry)
        {
            _host = host;
            _entry = entry;
        }

        public bool IsVisible => _host.IsOpen(_entry);

        public OverlayRequestOutcome RequestOpen() => _host.RequestOpen(_entry);

        public OverlayRequestOutcome RequestClose() => _host.RequestClose(_entry);

        // Unregisters without a close: the panel's own Dispose returns its leases.
        public void Dispose() => _host.Unregister(_entry);
    }
}
