#nullable enable
using System.Reflection;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// A Ten-Win Builds view that fails to build leaves the panel closed and the next open building
/// again: the panel drops the destroyed view and its screen before the new one is built, and
/// records the screen only once the build returned. The view destroys its partial sheet, opaque
/// and active until its last line, before the exception leaves. No device run can make the build
/// throw, and the view needs interop objects (ADR-0016), so the compiled order is held here
/// (ADR-0009: compiled artifacts, never source text). That Open and Relayout stop on the null
/// view a failed build returns is the compiler's: the result is nullable and warnings fail it.
/// </summary>
public sealed class AndroidLiveBuildLayoutGuardTests
{
    private const string Panel = "BazaarPlusPlus.Android.LiveBuild.AndroidLiveBuildPanel";
    private const string View = "BazaarPlusPlus.Android.LiveBuild.AndroidLiveBuildView";
    private const string State = "BazaarPlusPlus.Game.LiveBuildPanel.LiveBuildPanelState";
    private const string Debounce = "BazaarPlusPlus.Core.Ui.TouchRelayoutDebounce";

    private static readonly Lazy<CompiledAssembly> Host = new(() =>
        CompiledAssembly.Load(
            typeof(AndroidLiveBuildLayoutGuardTests)
                .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
                .Value!
        )
    );

    [Fact]
    public void A_failed_build_keeps_neither_the_destroyed_view_nor_the_new_screen()
    {
        var instructions = Method(Panel, "EnsureView").Instructions;
        var destroyed = Indexes(instructions, op => op.Targets(View, "Destroy")).Single();
        var built = Indexes(instructions, op => op.OpCode == "newobj" && op.Targets(View, ".ctor"))
            .Single();
        var views = Indexes(instructions, op => op.OpCode == "stfld" && op.Targets(Panel, "_view"));
        var screens = Indexes(
            instructions,
            op => op.OpCode == "stfld" && op.Targets(Panel, "_screenKey")
        );
        var laidOut = Indexes(instructions, op => op.Targets(Debounce, "LaidOutAt"));

        Assert.True(built > destroyed, "the new view is built after the old one is destroyed");
        Assert.True(
            views.Any(index => index > destroyed && index < built),
            "the destroyed view is dropped before the new one is built"
        );
        Assert.True(
            screens.Any(index => index > destroyed && index < built),
            "the old screen is dropped before the new view is built"
        );
        Assert.True(
            screens.Any(index => index > built),
            "the new screen is recorded after the build returned"
        );
        Assert.NotEmpty(laidOut);
        Assert.All(laidOut, index => Assert.True(index > built, "LaidOutAt follows the build"));
    }

    [Fact]
    public void A_failed_build_closes_an_open_panels_state_and_logs_it()
    {
        var method = Method(Panel, "EnsureView");

        Assert.Contains(method.Instructions, op => op.Targets(State, "Close"));
        Assert.Contains("android.live_build.layout_failed", method.Literals);
    }

    [Fact]
    public void A_view_that_throws_while_it_builds_destroys_its_partial_sheet()
    {
        Assert.Contains(
            Method(View, ".ctor").Instructions,
            op => op.Targets("UnityEngine.Object", "Destroy")
        );
    }

    private static CompiledMethod Method(string type, string name) =>
        Host
            .Value.Types.Single(candidate => candidate.FullName == type)
            .Methods.Single(method => method.Name == name && method.DeclaringType == type);

    private static IReadOnlyList<int> Indexes(
        IReadOnlyList<IlInstruction> instructions,
        Func<IlInstruction, bool> match
    ) =>
        Enumerable
            .Range(0, instructions.Count)
            .Where(index => match(instructions[index]))
            .ToArray();
}
