using BazaarPlusPlus.Game.OverlayPanels;

// Exe-runner checks for the pure Overlay Panel Host lifecycle rules (OverlayLifecycleCore).
// The Unity adapter (OverlayPanelHost) is intentionally out of scope: these tests pin the
// decision rules the three Main Overlay Panels used to duplicate in their Update methods.

const string SceneA = "SceneA|path|0|True";
const string SceneB = "SceneB|path|1|True";

TestFirstFrameAdoptsSceneTokenSilently();
TestHotkeyOpensAndClosesSamePanel();
TestOpeningSecondPanelClosesFirstBeforeOpening();
TestHotkeyOpenSuppressedInCombat();
TestCombatAutoClosesOpenPanel();
TestEscapeClosesOpenPanelOnly();
TestSceneChangeAlwaysPolicyClosesPanel();
TestSceneChangeOnlyWhenInCombatPolicyKeepsPanelOpen();
TestSceneChangeNotifiesEveryRegistrantAfterClose();
TestOpenRequestMatchesHotkeyOpenSemantics();
TestOpenRequestSuppressedInCombat();
TestCloseRequestClosesOnlyWhenOpen();
TestUnregisterClearsOpenPanel();

Console.WriteLine("OverlayPanelLifecycle checks passed.");

static OverlayLifecycleCore NewCoreWithPanels(
    SceneChangeClosePolicy historyPolicy = SceneChangeClosePolicy.OnlyWhenInCombat
)
{
    var core = new OverlayLifecycleCore();
    core.RegisterPanel("Collection", SceneChangeClosePolicy.Always);
    core.RegisterPanel("LiveBuild", SceneChangeClosePolicy.Always);
    core.RegisterPanel("History", historyPolicy);
    return core;
}

static OverlayFrameSnapshot Frame(
    string sceneToken = SceneA,
    bool isInCombat = false,
    bool escapePressed = false,
    string? hotkeyPressedPanelId = null
) =>
    new()
    {
        SceneToken = sceneToken,
        IsInCombat = isInCombat,
        EscapePressed = escapePressed,
        HotkeyPressedPanelId = hotkeyPressedPanelId,
    };

static void Prime(OverlayLifecycleCore core)
{
    // First frame adopts the scene token without emitting directives.
    core.Evaluate(Frame());
}

static void OpenViaHotkey(OverlayLifecycleCore core, string panelId)
{
    core.Evaluate(Frame(hotkeyPressedPanelId: panelId));
    Assert(core.OpenPanelId == panelId, $"Panel '{panelId}' should be open after hotkey.");
}

static void TestFirstFrameAdoptsSceneTokenSilently()
{
    var core = NewCoreWithPanels();
    var directives = core.Evaluate(Frame(sceneToken: SceneB));
    Assert(directives.Count == 0, "The first evaluated frame must not emit scene directives.");
    Assert(core.OpenPanelId == null, "No panel is open initially.");
}

static void TestHotkeyOpensAndClosesSamePanel()
{
    var core = NewCoreWithPanels();
    Prime(core);

    var openDirectives = core.Evaluate(Frame(hotkeyPressedPanelId: "Collection"));
    AssertDirective(openDirectives, 0, OverlayDirectiveKind.Open, "Collection");
    Assert(openDirectives.Count == 1, "Opening onto an empty band emits a single directive.");
    Assert(core.OpenPanelId == "Collection", "Collection should be open.");

    var closeDirectives = core.Evaluate(Frame(hotkeyPressedPanelId: "Collection"));
    AssertDirective(closeDirectives, 0, OverlayDirectiveKind.Close, "Collection");
    Assert(
        closeDirectives[0].CloseReason == OverlayCloseReason.HotkeyToggle,
        "Same-panel hotkey closes with the HotkeyToggle reason."
    );
    Assert(core.OpenPanelId == null, "Collection should be closed after the toggle.");
}

static void TestOpeningSecondPanelClosesFirstBeforeOpening()
{
    var core = NewCoreWithPanels();
    Prime(core);
    OpenViaHotkey(core, "Collection");

    var directives = core.Evaluate(Frame(hotkeyPressedPanelId: "History"));
    AssertDirective(directives, 0, OverlayDirectiveKind.Close, "Collection");
    Assert(
        directives[0].CloseReason == OverlayCloseReason.Superseded,
        "The displaced panel closes with the Superseded reason."
    );
    AssertDirective(directives, 1, OverlayDirectiveKind.Open, "History");
    Assert(core.OpenPanelId == "History", "History should be the open panel.");
}

static void TestHotkeyOpenSuppressedInCombat()
{
    var core = NewCoreWithPanels();
    Prime(core);

    var directives = core.Evaluate(Frame(isInCombat: true, hotkeyPressedPanelId: "LiveBuild"));
    Assert(directives.Count == 0, "Hotkey open is suppressed while combat is active.");
    Assert(core.OpenPanelId == null, "No panel opens in combat.");
}

static void TestCombatAutoClosesOpenPanel()
{
    var core = NewCoreWithPanels();
    Prime(core);
    OpenViaHotkey(core, "LiveBuild");

    var directives = core.Evaluate(Frame(isInCombat: true));
    AssertDirective(directives, 0, OverlayDirectiveKind.Close, "LiveBuild");
    Assert(
        directives[0].CloseReason == OverlayCloseReason.Combat,
        "Combat entry closes the open panel with the Combat reason."
    );
    Assert(core.OpenPanelId == null, "The panel is closed once combat starts.");
}

static void TestEscapeClosesOpenPanelOnly()
{
    var core = NewCoreWithPanels();
    Prime(core);

    var noOpenDirectives = core.Evaluate(Frame(escapePressed: true));
    Assert(noOpenDirectives.Count == 0, "Escape with no open panel is a no-op.");

    OpenViaHotkey(core, "Collection");
    var directives = core.Evaluate(Frame(escapePressed: true));
    AssertDirective(directives, 0, OverlayDirectiveKind.Close, "Collection");
    Assert(
        directives[0].CloseReason == OverlayCloseReason.Escape,
        "Escape closes with the Escape reason."
    );
    Assert(core.OpenPanelId == null, "Escape closed the panel.");
}

static void TestSceneChangeAlwaysPolicyClosesPanel()
{
    var core = NewCoreWithPanels();
    Prime(core);
    OpenViaHotkey(core, "Collection");

    var directives = core.Evaluate(Frame(sceneToken: SceneB));
    AssertDirective(directives, 0, OverlayDirectiveKind.Close, "Collection");
    Assert(
        directives[0].CloseReason == OverlayCloseReason.SceneChange,
        "Always-policy panels close on any scene change."
    );
    Assert(core.OpenPanelId == null, "Collection closed on scene change.");
}

static void TestSceneChangeOnlyWhenInCombatPolicyKeepsPanelOpen()
{
    var core = NewCoreWithPanels();
    Prime(core);
    OpenViaHotkey(core, "History");

    var nonCombat = core.Evaluate(Frame(sceneToken: SceneB));
    Assert(
        core.OpenPanelId == "History",
        "History stays open across a non-combat scene change (historical behavior)."
    );
    foreach (var directive in nonCombat)
        Assert(
            directive.Kind != OverlayDirectiveKind.Close,
            "Non-combat scene change must not close an OnlyWhenInCombat panel."
        );

    // A scene change while combat is active does close it (via the scene policy; the combat
    // auto-close rule would catch it on the same frame regardless).
    var combat = core.Evaluate(Frame(sceneToken: SceneA, isInCombat: true));
    Assert(
        combat[0].Kind == OverlayDirectiveKind.Close && combat[0].PanelId == "History",
        "Combat scene change closes the OnlyWhenInCombat panel."
    );
    Assert(core.OpenPanelId == null, "History closed on the combat scene change.");
}

static void TestSceneChangeNotifiesEveryRegistrantAfterClose()
{
    var core = NewCoreWithPanels();
    Prime(core);
    OpenViaHotkey(core, "Collection");

    var directives = core.Evaluate(Frame(sceneToken: SceneB));
    AssertDirective(directives, 0, OverlayDirectiveKind.Close, "Collection");

    var notified = directives
        .Where(directive => directive.Kind == OverlayDirectiveKind.NotifySceneChanged)
        .Select(directive => directive.PanelId)
        .ToArray();
    Assert(
        notified.SequenceEqual(new[] { "Collection", "LiveBuild", "History" }),
        "Scene-change side effects fan out to every registrant, open or closed, after the close."
    );
}

static void TestOpenRequestMatchesHotkeyOpenSemantics()
{
    var core = NewCoreWithPanels();
    Prime(core);
    OpenViaHotkey(core, "LiveBuild");

    var outcome = core.ExecuteOpenRequest("History", isInCombat: false, out var directives);
    Assert(outcome == OverlayRequestOutcome.Executed, "Open request executes out of combat.");
    AssertDirective(directives, 0, OverlayDirectiveKind.Close, "LiveBuild");
    AssertDirective(directives, 1, OverlayDirectiveKind.Open, "History");
    Assert(core.OpenPanelId == "History", "History opened synchronously via request.");

    var repeat = core.ExecuteOpenRequest("History", isInCombat: false, out var repeatDirectives);
    Assert(
        repeat == OverlayRequestOutcome.AlreadyInState && repeatDirectives.Count == 0,
        "Re-opening the already-open panel is a no-op."
    );

    var unknown = core.ExecuteOpenRequest("Nope", isInCombat: false, out _);
    Assert(unknown == OverlayRequestOutcome.UnknownPanel, "Unknown panel ids are rejected.");
}

static void TestOpenRequestSuppressedInCombat()
{
    var core = NewCoreWithPanels();
    Prime(core);

    var outcome = core.ExecuteOpenRequest("Collection", isInCombat: true, out var directives);
    Assert(
        outcome == OverlayRequestOutcome.SuppressedByCombat && directives.Count == 0,
        "External open requests respect the combat gate."
    );
    Assert(core.OpenPanelId == null, "Nothing opened in combat.");
}

static void TestCloseRequestClosesOnlyWhenOpen()
{
    var core = NewCoreWithPanels();
    Prime(core);

    var notOpen = core.ExecuteCloseRequest("Collection", out var noDirectives);
    Assert(
        notOpen == OverlayRequestOutcome.AlreadyInState && noDirectives.Count == 0,
        "Closing a panel that is not open is a no-op."
    );

    OpenViaHotkey(core, "Collection");
    var outcome = core.ExecuteCloseRequest("Collection", out var directives);
    Assert(outcome == OverlayRequestOutcome.Executed, "Close request executes when open.");
    AssertDirective(directives, 0, OverlayDirectiveKind.Close, "Collection");
    Assert(
        directives[0].CloseReason == OverlayCloseReason.Request,
        "Requested closes carry the Request reason."
    );
    Assert(core.OpenPanelId == null, "Collection closed via request.");
}

static void TestUnregisterClearsOpenPanel()
{
    var core = NewCoreWithPanels();
    Prime(core);
    OpenViaHotkey(core, "History");

    core.UnregisterPanel("History");
    Assert(core.OpenPanelId == null, "Unregistering the open panel clears the open state.");

    var directives = core.Evaluate(Frame(hotkeyPressedPanelId: "History"));
    Assert(directives.Count == 0, "Unregistered panels no longer respond to their hotkey.");
}

static void AssertDirective(
    IReadOnlyList<OverlayDirective> directives,
    int index,
    OverlayDirectiveKind kind,
    string panelId
)
{
    Assert(directives.Count > index, $"Expected at least {index + 1} directives.");
    Assert(
        directives[index].Kind == kind && directives[index].PanelId == panelId,
        $"Directive #{index} should be {kind} '{panelId}' but was "
            + $"{directives[index].Kind} '{directives[index].PanelId}'."
    );
}

static void Assert(bool condition, string message)
{
    if (condition)
        return;

    throw new InvalidOperationException($"FAILED: {message}");
}
