#nullable enable
using System.Reflection;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

// Device tests cannot induce a build failure, and the views require IL2CPP objects.
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

    [Fact]
    public void The_ten_win_builds_panel_compiles_into_the_host()
    {
        Assert.True(
            Host.Value.Types.Count(type => type.Namespace == "BazaarPlusPlus.Mobile.LiveBuild")
                >= 6,
            "Ten-Win Builds left the Android host's Mobile UI tree."
        );
    }

    [Fact]
    public void The_collection_panel_compiles_into_the_host()
    {
        Assert.True(
            Host.Value.Types.Count(type => type.Namespace == "BazaarPlusPlus.Mobile.Collection")
                >= 6,
            "Collection left the Android host's Mobile UI tree."
        );
    }

    [Fact]
    public void The_history_panel_compiles_into_the_host()
    {
        Assert.True(
            Host.Value.Types.Count(type => type.Namespace == "BazaarPlusPlus.Mobile.History") >= 16,
            "History left the Android host's Mobile UI tree."
        );
    }

    [Fact]
    public void The_hud_compiles_into_the_host()
    {
        Assert.True(
            Host.Value.Types.Count(type => type.Namespace == "BazaarPlusPlus.Mobile.Hud") >= 8,
            "The HUD left the Android host's Mobile UI tree."
        );
    }

    [Fact]
    public void The_preview_switches_compile_into_the_host()
    {
        Assert.True(
            Host.Value.Types.Count(type => type.Namespace == "BazaarPlusPlus.Mobile.Previews") >= 4,
            "The preview switches left the Android host's Mobile UI tree."
        );
    }

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
