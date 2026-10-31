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
    private const string LiveBuildPanel = "BazaarPlusPlus.Mobile.LiveBuild.TouchLiveBuildPanel";
    private const string LiveBuildView = "BazaarPlusPlus.Mobile.LiveBuild.TouchLiveBuildView";
    private const string LiveBuildInspection =
        "BazaarPlusPlus.Mobile.LiveBuild.TouchLiveBuildInspection";
    private const string CollectionPanel = "BazaarPlusPlus.Mobile.Collection.TouchCollectionPanel";
    private const string HistoryView = "BazaarPlusPlus.Mobile.History.TouchHistoryPanelView";

    private static readonly Lazy<CompiledAssembly> Host = new(() =>
        CompiledAssembly.Load(
            typeof(AndroidTouchViewBuildGuardTests)
                .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
                .Value!
        )
    );

    // Ten-Win Builds moved into the Mobile UI tree (ADR-0025): its six types must still compile
    // into the host, so the rules below cannot pass on a type that no longer exists.
    [Fact]
    public void The_ten_win_builds_panel_compiles_into_the_host()
    {
        Assert.True(
            Host.Value.Types.Count(type => type.Namespace == "BazaarPlusPlus.Mobile.LiveBuild")
                >= 6,
            "Ten-Win Builds left the Android host's Mobile UI tree."
        );
    }

    // Collection moved into the Mobile UI tree too: its six types must still compile into the host.
    [Fact]
    public void The_collection_panel_compiles_into_the_host()
    {
        Assert.True(
            Host.Value.Types.Count(type => type.Namespace == "BazaarPlusPlus.Mobile.Collection")
                >= 6,
            "Collection left the Android host's Mobile UI tree."
        );
    }

    // History moved into the Mobile UI tree too: its sixteen types must still compile into the host.
    [Fact]
    public void The_history_panel_compiles_into_the_host()
    {
        Assert.True(
            Host.Value.Types.Count(type => type.Namespace == "BazaarPlusPlus.Mobile.History") >= 16,
            "History left the Android host's Mobile UI tree."
        );
    }

    // The HUD (shortcuts and combat clock) moved into the Mobile UI tree too: its eight types must
    // still compile into the host.
    [Fact]
    public void The_hud_compiles_into_the_host()
    {
        Assert.True(
            Host.Value.Types.Count(type => type.Namespace == "BazaarPlusPlus.Mobile.Hud") >= 8,
            "The HUD left the Android host's Mobile UI tree."
        );
    }

    // The tooltip's preview switches and enchant preview moved into the Mobile UI tree too: both,
    // and the host reads they reach the tooltip through, must still compile into the host.
    [Fact]
    public void The_preview_switches_compile_into_the_host()
    {
        Assert.True(
            Host.Value.Types.Count(type => type.Namespace == "BazaarPlusPlus.Mobile.Previews") >= 4,
            "The preview switches left the Android host's Mobile UI tree."
        );
    }

    // Screen Fill's flow moved into the Mobile UI tree too (ADR-0025 decision 5): it, its day
    // clock and canvases, and the host read it reaches the game through must still compile into
    // the host.
    [Fact]
    public void Screen_fill_compiles_into_the_host()
    {
        Assert.True(
            Host.Value.Types.Count(type =>
                type.Namespace == "BazaarPlusPlus.Mobile.NativeAdjustment"
            ) >= 4,
            "Screen Fill left the Android host's Mobile UI tree."
        );
    }

    [Theory]
    [InlineData(LiveBuildView, ".ctor")]
    [InlineData(HistoryView, ".ctor")]
    [InlineData(CollectionPanel, "Build")]
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
