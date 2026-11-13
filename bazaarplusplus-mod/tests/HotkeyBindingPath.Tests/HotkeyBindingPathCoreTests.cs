using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Input;
using Xunit;

namespace HotkeyBindingPath.Tests;

// Each test is one check the capsule ran, with the same calls and messages; its unnamed blocks
// are named by the function they call. The capsule gathered every failure and threw once at the
// end; a test now stops at its first failure.
public sealed class HotkeyBindingPathCoreTests
{
    [Fact]
    public void CheckPrefixConstants()
    {
        CheckEqual("<Keyboard>/", HotkeyBindingPathCore.KeyboardPrefix, "Keyboard prefix constant");
        CheckEqual("<Mouse>/", HotkeyBindingPathCore.MousePrefix, "Mouse prefix constant");
    }

    [Fact]
    public void CheckBindingFailureGate()
    {
        var gate = new HotkeyBindingFailureGate<string>();
        var action = BppHotkeyActionId.ToggleHistoryPanel;

        Check(gate.ShouldReport(action, "invalid-a", "invalid"), "first binding failure reports");
        Check(
            !gate.ShouldReport(action, "invalid-a", "invalid"),
            "same binding failure is suppressed"
        );
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

    [Fact]
    public void CheckActivationState()
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

    [Fact]
    public void CheckStaleActionRelease()
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

    private static readonly Dictionary<string, (string? Input, string Expected)> NormalizeCases =
        new (string? Input, string Expected, string Name)[]
        {
            ("<Keyboard>/CTRL", "<Keyboard>/CTRL", "keyboard casing is preserved"),
            ("<Keyboard>/bogus", "<Keyboard>/bogus", "unknown keyboard controls stay accepted"),
            ("<Mouse>/scroll", string.Empty, "unsupported mouse scroll is rejected"),
            ("<Mouse>/LEFTBUTTON", "<Mouse>/leftButton", "mouse button casing is canonicalized"),
            ("not-a-binding", string.Empty, "garbage input is rejected"),
            (null, string.Empty, "null input is rejected"),
            ("   ", string.Empty, "blank input is rejected"),
        }.ToDictionary(testCase => testCase.Name, testCase => (testCase.Input, testCase.Expected));

    public static TheoryData<string> NormalizeCaseNames => new(NormalizeCases.Keys);

    [Theory]
    [MemberData(nameof(NormalizeCaseNames))]
    public void Normalize(string name)
    {
        var testCase = NormalizeCases[name];
        CheckEqual(
            testCase.Expected,
            HotkeyBindingPathCore.Normalize(testCase.Input),
            $"Normalize: {name}"
        );
    }

    [Fact]
    public void Expand_lists_each_modifier_alias()
    {
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
    }

    // One path table, changed between the checks.
    [Fact]
    public void FindConflict_compares_modifier_aliases()
    {
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
    }

    public static TheoryData<string> SupportedMouseButtonNames =>
        new(HotkeyBindingPathCore.SupportedMouseButtonNames);

    [Theory]
    [MemberData(nameof(SupportedMouseButtonNames))]
    public void DisplayAliases_name_every_supported_mouse_button(string mouseButtonName) =>
        Check(
            HotkeyBindingPathCore.DisplayAliases.ContainsKey("<Mouse>/" + mouseButtonName),
            $"DisplayAliases must contain supported mouse button '{mouseButtonName}'"
        );

    [Fact]
    public void Unsupported_mouse_paths_are_rejected_and_buttons_kept()
    {
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
    }

    private static void Check(bool condition, string message) => Assert.True(condition, message);

    private static void CheckEqual<T>(T expected, T actual, string message) =>
        Assert.True(
            EqualityComparer<T>.Default.Equals(expected, actual),
            $"{message}: expected '{expected}', got '{actual}'"
        );

    private static void CheckSequence(
        IEnumerable<string> expected,
        IEnumerable<string> actual,
        string message
    )
    {
        var expectedArray = expected.ToArray();
        var actualArray = actual.ToArray();
        Assert.True(
            expectedArray.SequenceEqual(actualArray, StringComparer.OrdinalIgnoreCase),
            $"{message}: expected [{string.Join(", ", expectedArray)}], got [{string.Join(", ", actualArray)}]"
        );
    }
}
