#nullable enable
using System.Reflection;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The half of the touch view rebuild rule Core's <c>TouchViewSlot</c> cannot hold: every touch
/// panel's build destroys its partial tree, opaque and active until its last lines, before the
/// exception leaves, so a failed build leaves nothing on screen. The slot's half (no stale view,
/// the panel reads closed, the next open builds again) is tested through the slot in
/// <c>SettingsDockCore.Tests</c>. No device run can make a build throw, and the views need
/// interop objects (ADR-0016), so the compiled calls are held here (ADR-0009: compiled
/// artifacts, never source text).
/// </summary>
public sealed class AndroidTouchViewBuildGuardTests
{
    private const string LiveBuildPanel = "BazaarPlusPlus.Android.LiveBuild.AndroidLiveBuildPanel";
    private const string LiveBuildView = "BazaarPlusPlus.Android.LiveBuild.AndroidLiveBuildView";
    private const string LiveBuildInspection =
        "BazaarPlusPlus.Android.LiveBuild.AndroidLiveBuildInspection";

    private static readonly Lazy<CompiledAssembly> Host = new(() =>
        CompiledAssembly.Load(
            typeof(AndroidTouchViewBuildGuardTests)
                .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
                .Value!
        )
    );

    [Theory]
    [InlineData(LiveBuildView, ".ctor")]
    [InlineData("BazaarPlusPlus.Android.Ui.AndroidHistoryPanelView", ".ctor")]
    [InlineData("BazaarPlusPlus.Android.Collection.AndroidCollectionPanel", "Build")]
    public void A_build_that_throws_destroys_its_partial_tree(string type, string method)
    {
        Assert.Contains(
            Method(type, method).Instructions,
            op => op.Targets("UnityEngine.Object", "Destroy")
        );
    }

    // The host closes the panel for a scene change, combat, the back key and the rest; every close
    // returns the view's cards, and with them the inspected card.
    [Fact]
    public void Closing_the_ten_win_builds_panel_closes_its_inspected_card()
    {
        Assert.Contains(
            Method(LiveBuildPanel, "Close").Instructions,
            op => op.Targets(LiveBuildView, "ReturnAll")
        );
        Assert.Contains(
            Method(LiveBuildView, "ReturnAll").Instructions,
            op => op.Targets(LiveBuildInspection, "Close")
        );
    }

    private static CompiledMethod Method(string type, string name) =>
        Host
            .Value.Types.Single(candidate => candidate.FullName == type)
            .Methods.Single(method => method.Name == name && method.DeclaringType == type);
}
