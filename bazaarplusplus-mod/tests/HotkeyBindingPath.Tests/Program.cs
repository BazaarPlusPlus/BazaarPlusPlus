using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Input;

var failures = new List<string>();

CheckPrefixConstants();
CheckBindingFailureGate();
CheckActivationState();
CheckStaleActionRelease();

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

void CheckEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        failures.Add($"{message}: expected '{expected}', got '{actual}'");
}

void CheckSequence(IEnumerable<string> expected, IEnumerable<string> actual, string message)
{
    var expectedArray = expected.ToArray();
    var actualArray = actual.ToArray();
    if (!expectedArray.SequenceEqual(actualArray, StringComparer.OrdinalIgnoreCase))
    {
        failures.Add(
            $"{message}: expected [{string.Join(", ", expectedArray)}], got [{string.Join(", ", actualArray)}]"
        );
    }
}

void CheckPrefixConstants()
{
    CheckEqual("<Keyboard>/", HotkeyBindingPathCore.KeyboardPrefix, "Keyboard prefix constant");
    CheckEqual("<Mouse>/", HotkeyBindingPathCore.MousePrefix, "Mouse prefix constant");
}

void CheckBindingFailureGate()
{
    var gate = new HotkeyBindingFailureGate<string>();
    var action = BppHotkeyActionId.ToggleHistoryPanel;

    Check(gate.ShouldReport(action, "invalid-a", "invalid"), "first binding failure reports");
    Check(!gate.ShouldReport(action, "invalid-a", "invalid"), "same binding failure is suppressed");
    Check(
        gate.ShouldReport(action, "invalid-b", "invalid"),
        "changed binding reopens the failure gate"
    );
    Check(
        gate.ShouldReport(action, "invalid-b", "unresolved"),
        "changed reason reopens the failure gate"
    );

    gate.Clear();
    Check(
        gate.ShouldReport(action, "invalid-a", "invalid"),
        "cleared lifecycle reopens the failure gate"
    );
}

void CheckStaleActionRelease()
{
    var configured = HotkeyBindingPathCore.DefaultBindingPaths.ToDictionary(
        pair => pair.Key,
        pair => (string?)pair.Value
    );
    string? Configured(BppHotkeyActionId actionId) => configured[actionId];

    const string F9 = "<Keyboard>/f9";
    const string F10 = "<Keyboard>/f10";
    var history = BppHotkeyActionId.ToggleHistoryPanel;
    var collection = BppHotkeyActionId.ToggleCollectionPanel;

    // A rebind from F9 to F10 leaves F9 unbound: its action is released.
    configured[history] = F10;
    Check(
        !HotkeyBindingPathCore.IsBoundToAnyAction(F9, Configured),
        "a path no hotkey resolves to is released"
    );
    Check(
        HotkeyBindingPathCore.IsBoundToAnyAction("<keyboard>/F10", Configured),
        "a rebound path stays bound, compared case-insensitively"
    );

    // Another action sharing the old path keeps it.
    configured[collection] = F9;
    Check(
        HotkeyBindingPathCore.IsBoundToAnyAction(F9, Configured),
        "a path another hotkey shares is kept"
    );

    // Swapping two bindings keeps both paths when either side is noticed first.
    configured[history] = F9;
    configured[collection] = F10;
    Check(
        HotkeyBindingPathCore.IsBoundToAnyAction(F10, Configured)
            && HotkeyBindingPathCore.IsBoundToAnyAction(F9, Configured),
        "swapped bindings keep both paths"
    );

    // An invalid configured value falls back to the default, which therefore stays bound.
    configured[history] = "not-a-binding";
    Check(
        HotkeyBindingPathCore.IsBoundToAnyAction(
            HotkeyBindingPathCore.GetDefault(history),
            Configured
        ),
        "an invalid value keeps its default path bound"
    );
}

void CheckActivationState()
{
    var state = new HotkeyActivationState();

    Check(
        !state.Resolve(HotkeyActivationMode.Hold, "shift", false, false, 1),
        "hold mode is inactive while released"
    );
    Check(
        state.Resolve(HotkeyActivationMode.Hold, "shift", true, true, 2),
        "hold mode follows the physical key"
    );
    Check(
        state.Resolve(HotkeyActivationMode.Toggle, "shift", false, true, 3),
        "first toggle press activates"
    );
    Check(
        state.Resolve(HotkeyActivationMode.Toggle, "shift", false, true, 3),
        "multiple readers cannot toggle twice in one frame"
    );
    Check(
        state.Resolve(HotkeyActivationMode.Toggle, "shift", false, false, 4),
        "toggle remains active after release"
    );
    Check(
        !state.Resolve(HotkeyActivationMode.Toggle, "shift", false, true, 5),
        "second toggle press deactivates"
    );
    Check(
        !state.Resolve(HotkeyActivationMode.Toggle, "f6", false, false, 6),
        "changing the binding clears the latch"
    );
    Check(
        state.Resolve(HotkeyActivationMode.Toggle, "f6", false, true, 7),
        "new binding can activate after reset"
    );
    Check(
        !state.Resolve(HotkeyActivationMode.Hold, "f6", false, false, 8),
        "changing back to hold clears the latch"
    );
}

var normalizeCases = new (string? Input, string Expected, string Name)[]
{
    ("<Keyboard>/CTRL", "<Keyboard>/CTRL", "keyboard casing is preserved"),
    ("<Keyboard>/bogus", "<Keyboard>/bogus", "unknown keyboard controls stay accepted"),
    ("<Mouse>/scroll", string.Empty, "unsupported mouse scroll is rejected"),
    ("<Mouse>/LEFTBUTTON", "<Mouse>/leftButton", "mouse button casing is canonicalized"),
    ("not-a-binding", string.Empty, "garbage input is rejected"),
    (null, string.Empty, "null input is rejected"),
    ("   ", string.Empty, "blank input is rejected"),
};

foreach (var testCase in normalizeCases)
{
    CheckEqual(
        testCase.Expected,
        HotkeyBindingPathCore.Normalize(testCase.Input),
        $"Normalize: {testCase.Name}"
    );
}

CheckSequence(
    ["<Keyboard>/ctrl", "<Keyboard>/leftCtrl", "<Keyboard>/rightCtrl"],
    HotkeyBindingPathCore.Expand("<Keyboard>/ctrl"),
    "Expand: Ctrl alias"
);
CheckSequence(
    ["<Keyboard>/shift", "<Keyboard>/leftShift", "<Keyboard>/rightShift"],
    HotkeyBindingPathCore.Expand("<Keyboard>/shift"),
    "Expand: Shift alias"
);
CheckSequence(
    ["<Keyboard>/f8"],
    HotkeyBindingPathCore.Expand("<Keyboard>/f8"),
    "Expand: ordinary key"
);

var currentPaths = new Dictionary<BppHotkeyActionId, string>
{
    [BppHotkeyActionId.HoldEnchantPreview] = "<Keyboard>/ctrl",
    [BppHotkeyActionId.HoldUpgradePreview] = "<Keyboard>/shift",
    [BppHotkeyActionId.ToggleCollectionPanel] = "<Keyboard>/tab",
    [BppHotkeyActionId.ToggleLiveBuildPanel] = "<Keyboard>/capsLock",
    [BppHotkeyActionId.ToggleHistoryPanel] = "<Keyboard>/f8",
};

CheckEqual(
    BppHotkeyActionId.HoldEnchantPreview,
    HotkeyBindingPathCore.FindConflict(
        BppHotkeyActionId.ToggleHistoryPanel,
        "<Keyboard>/leftCtrl",
        currentPaths
    ),
    "FindConflict: leftCtrl conflicts with Ctrl alias"
);

currentPaths[BppHotkeyActionId.HoldEnchantPreview] = "<Keyboard>/leftCtrl";
CheckEqual(
    BppHotkeyActionId.HoldEnchantPreview,
    HotkeyBindingPathCore.FindConflict(
        BppHotkeyActionId.ToggleHistoryPanel,
        "<Keyboard>/ctrl",
        currentPaths
    ),
    "FindConflict: Ctrl alias conflicts with leftCtrl"
);

CheckEqual<BppHotkeyActionId?>(
    null,
    HotkeyBindingPathCore.FindConflict(
        BppHotkeyActionId.HoldEnchantPreview,
        "<Keyboard>/leftCtrl",
        currentPaths
    ),
    "FindConflict: candidate action is skipped"
);
CheckEqual<BppHotkeyActionId?>(
    null,
    HotkeyBindingPathCore.FindConflict(
        BppHotkeyActionId.ToggleHistoryPanel,
        "<Keyboard>/f9",
        currentPaths
    ),
    "FindConflict: unrelated path has no conflict"
);

foreach (var mouseButtonName in HotkeyBindingPathCore.SupportedMouseButtonNames)
{
    Check(
        HotkeyBindingPathCore.DisplayAliases.ContainsKey("<Mouse>/" + mouseButtonName),
        $"DisplayAliases must contain supported mouse button '{mouseButtonName}'"
    );
}

Check(
    HotkeyBindingPathCore.IsExplicitlyUnsupportedMousePath("<Mouse>/scroll"),
    "Unsupported mouse paths: scroll"
);
Check(
    HotkeyBindingPathCore.IsExplicitlyUnsupportedMousePath("<Mouse>/position"),
    "Unsupported mouse paths: position"
);
Check(
    HotkeyBindingPathCore.IsExplicitlyUnsupportedMousePath("<Mouse>/delta"),
    "Unsupported mouse paths: delta"
);
Check(
    !HotkeyBindingPathCore.IsExplicitlyUnsupportedMousePath("<Mouse>/leftButton"),
    "Unsupported mouse paths: standard button stays supported"
);

if (failures.Count > 0)
{
    throw new InvalidOperationException(
        $"FAILED ({failures.Count}){Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Select(failure => $"- {failure}"))
    );
}
else
{
    Console.WriteLine("PASS: HotkeyBindingPathCore behavior checks");
}
