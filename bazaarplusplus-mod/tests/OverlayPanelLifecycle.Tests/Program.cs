using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

// Exe-runner checks for the Overlay Panel Host. Every test drives only the public
// OverlayPanelHost through fake registrations and reads what the panels were told.

var logLines = new List<string>();
BppLog.Install(new ListLogSink(logLines));

Run(TestFirstSceneTokenIsAdoptedSilently);
Run(TestToggleOpensAndClosesTheSamePanel);
Run(TestTogglingAnotherPanelSupersedesTheOpenOne);
Run(TestToggleOpenIsSuppressedInCombat);
Run(TestCombatClosesTheOpenPanel);
Run(TestEscapeClosesTheOpenPanelOnly);
Run(TestModalInputDismissesOnEscapeAndIgnoresTheToggle);
Run(TestSceneChangeClosesAnAlwaysPanel);
Run(TestSceneChangeClosesAnOnlyWhenInCombatPanelOnlyInCombat);
Run(TestSceneChangeNotifiesEveryRegistrantAfterTheClose);
Run(TestOpenRequestSupersedesTheOpenPanel);
Run(TestOpenRequestIsSuppressedInCombat);
Run(TestRequestsForUnknownPanelsOrTheCurrentStateAreNoOps);
Run(TestDisposingAHandleUnregistersWithoutAClose);
Run(TestSwitchOffClosesAnOpenPanelThenNotifiesIt);
Run(TestSwitchOffOfAClosedPanelOnlyNotifiesIt);
Run(TestTheFirstEnabledValueIsAdoptedSilently);
Run(TestASwitchOffBeforeTheFirstFrameIsAnEdge);
Run(TestADisabledPanelNeverOpens);
Run(TestSwitchingBackOnDoesNotOpen);
Run(TestReleaseRunsAfterTheClosesAndSceneNotifications);
Run(TestReleaseRunsOnceWhenASceneChangeAndASwitchOffLandTogether);
Run(TestReleaseRunsOnASwitchOffOfAClosedPanel);
Run(TestPlainClosesNeverRelease);
Run(TestOpenReturningFalseLeavesThePanelClosedWithoutAClose);
Run(() => TestOpenThrowingClosesWithOpenFailedAndLogs(logLines));
Run(TestEscapeReportsWhatItConsumed);
Run(TestObserveSceneAppliesTheScenePolicyWithoutTicking);
Run(() => TestRequestsFromLifecycleCallbacksAreBusy(logLines));
Run(TestRequestsFromTicksExecute);
Run(TestCombatIsReadOnlyWhenARuleCanUseIt);
Run(TestATickSeesARequestMadeByAnEarlierTick);
Run(() => TestAFailingCallbackDoesNotStopTheNextPanel(logLines));

Console.WriteLine("OverlayPanelLifecycle checks passed.");

void Run(Action test)
{
    logLines.Clear();
    test();
}

static void TestFirstSceneTokenIsAdoptedSilently()
{
    var rig = new Rig();
    var collection = rig.Add("collection");
    rig.Frame(Rig.SceneB);
    Check.Events(rig, [], "The first scene token is adopted without a scene change.");
    Check.That(rig.Host.OpenPanelId == null, "No panel is open initially.");
    Check.That(collection.Ticks.SequenceEqual([false]), "Every frame ticks every registrant.");
}

static void TestToggleOpensAndClosesTheSamePanel()
{
    var rig = new Rig();
    rig.Add("collection");
    rig.Frame();
    rig.Frame(toggle: "collection");
    Check.That(rig.Host.OpenPanelId == "collection", "The toggle opens the panel.");
    rig.Frame(toggle: "collection");
    Check.Events(
        rig,
        ["open:collection", "close:collection:HotkeyToggle"],
        "A toggle of the open panel closes it."
    );
    Check.That(rig.Host.OpenPanelId == null, "The panel is closed after the second toggle.");
}

static void TestTogglingAnotherPanelSupersedesTheOpenOne()
{
    var rig = new Rig();
    rig.Add("collection");
    rig.Add("history", SceneChangeClosePolicy.OnlyWhenInCombat);
    rig.Frame();
    rig.Frame(toggle: "collection");
    rig.Frame(toggle: "history");
    Check.Events(
        rig,
        ["open:collection", "close:collection:Superseded", "open:history"],
        "The displaced panel closes before the toggled one opens."
    );
    Check.That(rig.Host.OpenPanelId == "history", "History is the open panel.");
}

static void TestToggleOpenIsSuppressedInCombat()
{
    var rig = new Rig();
    rig.Add("live_build");
    rig.Frame();
    rig.InCombat = true;
    rig.Frame(toggle: "live_build");
    Check.Events(rig, [], "A toggle does not open a panel in combat.");
}

static void TestCombatClosesTheOpenPanel()
{
    var rig = new Rig();
    rig.Add("live_build");
    rig.Frame();
    rig.Frame(toggle: "live_build");
    rig.InCombat = true;
    rig.Frame();
    Check.Events(
        rig,
        ["open:live_build", "close:live_build:Combat"],
        "Combat closes the open panel with the Combat reason."
    );
}

static void TestEscapeClosesTheOpenPanelOnly()
{
    var rig = new Rig();
    rig.Add("collection");
    rig.Add("history");
    rig.Frame();
    rig.Frame(escape: true);
    Check.Events(rig, [], "Escape with no open panel does nothing.");
    rig.Frame(toggle: "collection");
    rig.Frame(escape: true);
    Check.Events(
        rig,
        ["open:collection", "close:collection:Escape"],
        "Escape closes the open panel with the Escape reason."
    );
}

static void TestModalInputDismissesOnEscapeAndIgnoresTheToggle()
{
    var rig = new Rig();
    var collection = rig.Add("collection");
    rig.Add("history");
    rig.Frame();
    rig.Frame(toggle: "collection");
    collection.Modal = true;
    rig.Frame(escape: true, toggle: "history");
    rig.Frame(toggle: "history");
    Check.Events(
        rig,
        ["open:collection", "dismiss:collection"],
        "Escape dismisses only the modal, and toggles are ignored while it is up."
    );
    rig.InCombat = true;
    rig.Frame();
    Check.That(
        rig.Host.OpenPanelId == null && rig.Events[^1] == "close:collection:Combat",
        "A modal cannot hold the panel open into combat."
    );
}

static void TestSceneChangeClosesAnAlwaysPanel()
{
    var rig = new Rig();
    rig.Add("collection");
    rig.Frame();
    rig.Frame(toggle: "collection");
    rig.Frame(Rig.SceneB);
    Check.Events(
        rig,
        [
            "open:collection",
            "close:collection:SceneChange",
            "scene:collection",
            "release:SceneChange",
        ],
        "An Always panel closes on any scene change."
    );
}

static void TestSceneChangeClosesAnOnlyWhenInCombatPanelOnlyInCombat()
{
    var rig = new Rig();
    rig.Add("history", SceneChangeClosePolicy.OnlyWhenInCombat);
    rig.Frame();
    rig.Frame(toggle: "history");
    rig.Frame(Rig.SceneB);
    Check.That(rig.Host.OpenPanelId == "history", "History stays open across a calm scene.");
    rig.InCombat = true;
    rig.Frame(Rig.SceneA);
    Check.Events(
        rig,
        [
            "open:history",
            "scene:history",
            "release:SceneChange",
            "close:history:SceneChange",
            "scene:history",
            "release:SceneChange",
        ],
        "A scene change in combat closes an OnlyWhenInCombat panel by its scene policy."
    );
}

static void TestSceneChangeNotifiesEveryRegistrantAfterTheClose()
{
    var rig = new Rig();
    rig.Add("collection");
    rig.Add("live_build");
    rig.Add("history", SceneChangeClosePolicy.OnlyWhenInCombat);
    rig.Frame();
    rig.Frame(toggle: "collection");
    rig.Frame(Rig.SceneB);
    Check.Events(
        rig,
        [
            "open:collection",
            "close:collection:SceneChange",
            "scene:collection",
            "scene:live_build",
            "scene:history",
            "release:SceneChange",
        ],
        "Scene-change side effects reach every registrant, open or closed, after the close."
    );
}

static void TestOpenRequestSupersedesTheOpenPanel()
{
    var rig = new Rig();
    rig.Add("live_build");
    var history = rig.Add("history");
    rig.Frame();
    rig.Frame(toggle: "live_build");
    var outcome = history.Handle.RequestOpen();
    Check.That(outcome == OverlayRequestOutcome.Executed, "An open request executes.");
    Check.Events(
        rig,
        ["open:live_build", "close:live_build:Superseded", "open:history"],
        "An open request closes the open panel before it opens."
    );
    Check.That(history.Handle.IsVisible, "The handle reports the panel visible.");
}

static void TestOpenRequestIsSuppressedInCombat()
{
    var rig = new Rig();
    var collection = rig.Add("collection");
    rig.InCombat = true;
    Check.That(
        collection.Handle.RequestOpen() == OverlayRequestOutcome.SuppressedByCombat,
        "An open request respects the combat gate."
    );
    Check.Events(rig, [], "Nothing opens in combat.");
}

static void TestRequestsForUnknownPanelsOrTheCurrentStateAreNoOps()
{
    var rig = new Rig();
    var collection = rig.Add("collection");
    Check.That(
        collection.Handle.RequestClose() == OverlayRequestOutcome.AlreadyInState,
        "Closing a closed panel is a no-op."
    );
    collection.Handle.RequestOpen();
    Check.That(
        collection.Handle.RequestOpen() == OverlayRequestOutcome.AlreadyInState,
        "Opening the open panel is a no-op."
    );
    Check.That(
        collection.Handle.RequestClose() == OverlayRequestOutcome.Executed,
        "Closing the open panel executes."
    );
    collection.Handle.Dispose();
    Check.That(
        collection.Handle.RequestOpen() == OverlayRequestOutcome.UnknownPanel,
        "A disposed handle names an unknown panel."
    );
    Check.Events(
        rig,
        ["open:collection", "close:collection:Request"],
        "Requests carry the Request close reason."
    );
}

static void TestDisposingAHandleUnregistersWithoutAClose()
{
    var rig = new Rig();
    var history = rig.Add("history");
    rig.Frame();
    rig.Frame(toggle: "history");
    history.Handle.Dispose();
    Check.That(rig.Host.OpenPanelId == null, "Unregistering the open panel clears it.");
    rig.Frame(toggle: "history");
    Check.Events(rig, ["open:history"], "Dispose does not close, and the toggle is gone.");
    Check.That(history.Ticks.Count == 2, "An unregistered panel no longer ticks.");
}

static void TestSwitchOffClosesAnOpenPanelThenNotifiesIt()
{
    var rig = new Rig();
    var history = rig.Add("history", switchable: true);
    rig.Frame();
    history.Handle.RequestOpen();
    history.Enabled = false;
    rig.Frame();
    Check.Events(
        rig,
        ["open:history", "close:history:SwitchedOff", "off:history", "release:SwitchedOff"],
        "A switch-off closes the open panel, then notifies it, then releases."
    );
    rig.Frame();
    Check.That(rig.Events.Count == 4, "A switch-off fires once per edge.");
}

static void TestSwitchOffOfAClosedPanelOnlyNotifiesIt()
{
    var rig = new Rig();
    var collection = rig.Add("collection", switchable: true);
    rig.Add("history");
    rig.Frame();
    rig.Frame(toggle: "history");
    collection.Enabled = false;
    rig.Frame();
    Check.Events(
        rig,
        ["open:history", "off:collection", "release:SwitchedOff"],
        "A closed panel's switch-off notifies it and leaves the open panel alone."
    );
}

static void TestTheFirstEnabledValueIsAdoptedSilently()
{
    var rig = new Rig();
    rig.Add("collection", switchable: true, enabled: false);
    rig.Frame();
    rig.Frame();
    Check.Events(rig, [], "A panel that starts off has no switch-off edge.");
}

static void TestASwitchOffBeforeTheFirstFrameIsAnEdge()
{
    var rig = new Rig();
    var collection = rig.Add("collection", switchable: true);
    Check.That(collection.Handle.RequestOpen() == OverlayRequestOutcome.Executed, "open");
    collection.Enabled = false;
    rig.Frame();
    Check.Events(
        rig,
        [
            "open:collection",
            "close:collection:SwitchedOff",
            "off:collection",
            "release:SwitchedOff",
        ],
        "Register adopts the first value, so a switch-off before the first frame still closes."
    );
}

static void TestADisabledPanelNeverOpens()
{
    var rig = new Rig();
    var collection = rig.Add("collection", switchable: true, enabled: false);
    rig.InCombat = true;
    Check.That(
        collection.Handle.RequestOpen() == OverlayRequestOutcome.Disabled,
        "A disabled panel's open request says Disabled before the combat gate."
    );
    rig.InCombat = false;
    rig.Frame();
    rig.Frame(toggle: "collection");
    Check.Events(rig, [], "A disabled panel ignores its toggle.");
}

static void TestSwitchingBackOnDoesNotOpen()
{
    var rig = new Rig();
    var collection = rig.Add("collection", switchable: true);
    rig.Frame();
    collection.Enabled = false;
    rig.Frame();
    collection.Enabled = true;
    rig.Frame();
    Check.Events(
        rig,
        ["off:collection", "release:SwitchedOff"],
        "A false-to-true edge does nothing; the panel opens only on a request."
    );
}

static void TestReleaseRunsAfterTheClosesAndSceneNotifications()
{
    var rig = new Rig();
    rig.Add("collection");
    rig.Add("history");
    rig.Frame();
    rig.Frame(toggle: "history");
    rig.Frame(Rig.SceneB);
    Check.That(
        rig.Events[^1] == "release:SceneChange"
            && rig.Events.Count(e => e.StartsWith("release:", StringComparison.Ordinal)) == 1,
        "The release runs once, last, after every panel let go of its own state."
    );
}

static void TestReleaseRunsOnceWhenASceneChangeAndASwitchOffLandTogether()
{
    var rig = new Rig();
    var collection = rig.Add("collection", switchable: true);
    rig.Frame();
    collection.Handle.RequestOpen();
    collection.Enabled = false;
    rig.Frame(Rig.SceneB);
    Check.Events(
        rig,
        [
            "open:collection",
            "close:collection:SceneChange",
            "scene:collection",
            "off:collection",
            "release:SceneChange",
        ],
        "One release with the SceneChange reason covers both edges."
    );
}

static void TestReleaseRunsOnASwitchOffOfAClosedPanel()
{
    var rig = new Rig();
    var collection = rig.Add("collection", switchable: true);
    rig.Frame();
    collection.Enabled = false;
    rig.Frame();
    Check.That(rig.Events[^1] == "release:SwitchedOff", "A closed panel's switch-off releases.");
}

static void TestPlainClosesNeverRelease()
{
    var rig = new Rig();
    var collection = rig.Add("collection");
    var history = rig.Add("history");
    rig.Frame();
    collection.Handle.RequestOpen();
    collection.Handle.RequestClose();
    collection.Handle.RequestOpen();
    history.Handle.RequestOpen();
    rig.Frame(escape: true);
    rig.Frame(toggle: "collection");
    rig.InCombat = true;
    rig.Frame();
    Check.That(
        rig.Events.All(e => !e.StartsWith("release:", StringComparison.Ordinal)),
        "Request, Superseded, Escape and Combat closes keep the Host's resources."
    );
    Check.That(rig.Events[^1] == "close:collection:Combat", "The last close was the combat one.");
}

static void TestOpenReturningFalseLeavesThePanelClosedWithoutAClose()
{
    var rig = new Rig();
    var collection = rig.Add("collection");
    collection.OpenResult = () => false;
    Check.That(
        collection.Handle.RequestOpen() == OverlayRequestOutcome.OpenFailed,
        "A panel that could not open reports OpenFailed."
    );
    rig.Frame();
    rig.Frame(toggle: "collection");
    Check.Events(rig, ["open:collection", "open:collection"], "A clean false is not closed.");
    Check.That(rig.Host.OpenPanelId == null, "The panel stays closed.");
}

static void TestOpenThrowingClosesWithOpenFailedAndLogs(List<string> logLines)
{
    var rig = new Rig();
    var collection = rig.Add("collection");
    collection.OpenResult = () => throw new InvalidOperationException("boom");
    Check.That(
        collection.Handle.RequestOpen() == OverlayRequestOutcome.OpenFailed,
        "A throwing open reports OpenFailed."
    );
    Check.Events(
        rig,
        ["open:collection", "close:collection:OpenFailed"],
        "A throwing open gets one close so it can return partial leases."
    );
    Check.That(rig.Host.OpenPanelId == null, "The panel stays closed.");
    Check.That(
        logLines.Count(line => line.Contains("overlay_panels.directive.failed")) == 1,
        "The failed open is logged."
    );
    collection.OpenResult = () => true;
    Check.That(
        collection.Handle.RequestOpen() == OverlayRequestOutcome.Executed,
        "The next open works."
    );
}

static void TestEscapeReportsWhatItConsumed()
{
    var rig = new Rig();
    var collection = rig.Add("collection");
    Check.That(!rig.Host.Escape().Consumed, "Escape with nothing open is not consumed.");
    collection.Handle.RequestOpen();
    collection.Modal = true;
    Check.That(
        rig.Host.Escape()
            == new OverlayEscapeOutcome(OverlayEscapeKind.DismissedModal, "collection"),
        "Escape dismisses the modal first."
    );
    collection.Modal = false;
    Check.That(
        rig.Host.Escape() == new OverlayEscapeOutcome(OverlayEscapeKind.Closed, "collection"),
        "Escape then closes the panel."
    );
    Check.Events(
        rig,
        ["open:collection", "dismiss:collection", "close:collection:Escape"],
        "Escape runs the modal rule, then the escape close."
    );
}

static void TestObserveSceneAppliesTheScenePolicyWithoutTicking()
{
    var rig = new Rig();
    var collection = rig.Add("collection");
    var history = rig.Add("history", SceneChangeClosePolicy.OnlyWhenInCombat);
    rig.Host.ObserveScene("0");
    rig.Host.ObserveScene("0");
    history.Handle.RequestOpen();
    rig.Host.ObserveScene("1");
    Check.That(history.Handle.IsVisible, "History survives a calm scene observation.");
    collection.Handle.RequestOpen();
    rig.Host.ObserveScene("2");
    Check.Events(
        rig,
        [
            "open:history",
            "scene:collection",
            "scene:history",
            "release:SceneChange",
            "close:history:Superseded",
            "open:collection",
            "close:collection:SceneChange",
            "scene:collection",
            "scene:history",
            "release:SceneChange",
        ],
        "The same token is a no-op; each policy closes as on a frame."
    );
    Check.That(collection.Ticks.Count == 0, "A scene observation does not tick.");
}

static void TestRequestsFromLifecycleCallbacksAreBusy(List<string> logLines)
{
    var rig = new Rig();
    var collection = rig.Add("collection");
    var history = rig.Add("history");
    var outcomes = new List<OverlayRequestOutcome>();
    collection.OnCloseHook = () =>
    {
        outcomes.Add(collection.Handle.RequestClose());
        outcomes.Add(history.Handle.RequestOpen());
    };
    history.OnSceneHook = () => outcomes.Add(history.Handle.RequestOpen());
    rig.Frame();
    collection.Handle.RequestOpen();
    collection.Handle.RequestClose();
    Check.That(
        outcomes.SequenceEqual([OverlayRequestOutcome.AlreadyInState, OverlayRequestOutcome.Busy]),
        "Closing the closing panel is AlreadyInState; any other request from onClose is Busy."
    );
    rig.Frame(Rig.SceneB);
    Check.That(
        outcomes[^1] == OverlayRequestOutcome.Busy,
        "A request from OnSceneChanged is Busy."
    );
    Check.That(
        logLines.Count(line => line.Contains("overlay_panels.request.reentrant")) == 2,
        "Only the Busy requests are logged."
    );
}

static void TestRequestsFromTicksExecute()
{
    var rig = new Rig();
    var collection = rig.Add("collection");
    var outcome = OverlayRequestOutcome.UnknownPanel;
    collection.TickHook = (_, isOpen) =>
    {
        if (!isOpen)
            outcome = collection.Handle.RequestOpen();
    };
    rig.Frame();
    Check.That(outcome == OverlayRequestOutcome.Executed, "A request from a tick executes.");
}

static void TestCombatIsReadOnlyWhenARuleCanUseIt()
{
    var rig = new Rig();
    var collection = rig.Add("collection", switchable: true);
    rig.Frame();
    rig.Frame(Rig.SceneB, escape: true);
    collection.Enabled = false;
    collection.Handle.RequestOpen();
    rig.Host.ObserveScene("x");
    Check.That(rig.CombatReads == 0, "Nothing open and no toggle or open: combat is not read.");
    collection.Enabled = true;
    rig.Frame(toggle: "collection");
    Check.That(rig.CombatReads == 1, "A pending toggle reads combat once.");
    rig.Frame();
    Check.That(rig.CombatReads == 2, "An open panel reads combat each frame.");
}

static void TestATickSeesARequestMadeByAnEarlierTick()
{
    var rig = new Rig();
    var a = rig.Add("a");
    var b = rig.Add("b");
    var requested = false;
    a.TickHook = (_, _) =>
    {
        if (requested)
            return;
        requested = true;
        b.Handle.RequestOpen();
    };
    rig.Frame();
    Check.That(b.Ticks.SequenceEqual([true]), "B's tick in the same frame sees itself open.");
    Check.That(a.Ticks.SequenceEqual([false]) && !a.Handle.IsVisible, "A stays closed.");
}

static void TestAFailingCallbackDoesNotStopTheNextPanel(List<string> logLines)
{
    var rig = new Rig();
    var failing = rig.Add("failing_panel");
    var next = rig.Add("next_panel");
    failing.TickHook = (_, _) => throw new InvalidOperationException("tick");
    failing.OnSceneHook = () => throw new InvalidOperationException("scene");
    rig.Frame();
    rig.Frame(Rig.SceneB);
    Check.That(next.Ticks.Count == 2, "The next panel still ticks.");
    Check.That(rig.Events.Contains("scene:next_panel"), "The next panel still hears the scene.");
    Check.That(
        logLines.Any(line => line.Contains("overlay_panels.host.tick_degraded"))
            && logLines.Any(line => line.Contains("overlay_panels.directive.failed")),
        "Both failures are logged."
    );
}

internal sealed class Rig
{
    public const string SceneA = "SceneA|path|0|True";
    public const string SceneB = "SceneB|path|1|True";

    public Rig()
    {
        Host = new OverlayPanelHost(
            () =>
            {
                CombatReads++;
                return InCombat;
            },
            reason => Events.Add($"release:{reason}")
        );
    }

    public OverlayPanelHost Host { get; }
    public List<string> Events { get; } = new();
    public bool InCombat { get; set; }
    public int CombatReads { get; private set; }

    public FakePanel Add(
        string id,
        SceneChangeClosePolicy policy = SceneChangeClosePolicy.Always,
        bool switchable = false,
        bool enabled = true
    ) => new(this, id, policy, switchable, enabled);

    public void Frame(string scene = SceneA, bool escape = false, string? toggle = null) =>
        Host.Frame(new OverlayPanelFrame(scene, 0.016f, escape, toggle));
}

internal sealed class FakePanel
{
    public FakePanel(
        Rig rig,
        string id,
        SceneChangeClosePolicy policy,
        bool switchable,
        bool enabled
    )
    {
        Enabled = enabled;
        Handle = rig.Host.Register(
            new OverlayPanelRegistration(
                id,
                onOpen: () =>
                {
                    rig.Events.Add($"open:{id}");
                    return OpenResult();
                },
                onClose: reason =>
                {
                    rig.Events.Add($"close:{id}:{reason}");
                    OnCloseHook?.Invoke();
                },
                tick: (dt, isOpen) =>
                {
                    Ticks.Add(isOpen);
                    TickHook?.Invoke(dt, isOpen);
                }
            )
            {
                SceneChangeClose = policy,
                Enabled = switchable ? () => Enabled : null,
                IsModalInputActive = () => Modal,
                DismissModal = () => rig.Events.Add($"dismiss:{id}"),
                OnSceneChanged = () =>
                {
                    rig.Events.Add($"scene:{id}");
                    OnSceneHook?.Invoke();
                },
                OnSwitchedOff = () => rig.Events.Add($"off:{id}"),
            }
        );
    }

    public IOverlayPanelHandle Handle { get; }
    public bool Enabled { get; set; } = true;
    public bool Modal { get; set; }
    public Func<bool> OpenResult { get; set; } = () => true;
    public Action? OnCloseHook { get; set; }
    public Action? OnSceneHook { get; set; }
    public Action<float, bool>? TickHook { get; set; }
    public List<bool> Ticks { get; } = new();
}

internal static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"FAILED: {message}");
    }

    public static void Events(Rig rig, string[] expected, string message)
    {
        if (!rig.Events.SequenceEqual(expected))
            throw new InvalidOperationException(
                $"FAILED: {message} Expected [{string.Join(", ", expected)}], got "
                    + $"[{string.Join(", ", rig.Events)}]."
            );
    }
}

internal sealed class ListLogSink(List<string> lines) : IBppLogSink
{
    public void Write(BppLogSeverity severity, string message) => lines.Add(message);
}
