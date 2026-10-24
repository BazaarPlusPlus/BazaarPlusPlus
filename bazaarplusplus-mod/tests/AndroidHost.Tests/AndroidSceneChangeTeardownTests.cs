#nullable enable
using System.Reflection;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// A scene change the game starts closes every touch panel, an inspected Ten-Win Builds card
/// included, before the rental releases what is left. On the emulator the game changes scene by
/// itself only after an offline Resume, when no run has loaded and Ten-Win Builds has no card to
/// inspect, so the compiled call chain from <c>AndroidTouchUi.OnSceneLoaded</c> to the inspection's
/// close is held here (ADR-0009: compiled artifacts, never source text).
/// </summary>
public sealed class AndroidSceneChangeTeardownTests
{
    private const string TouchUi = "BazaarPlusPlus.Android.Ui.AndroidTouchUi";
    private const string Panel = "BazaarPlusPlus.Android.LiveBuild.AndroidLiveBuildPanel";
    private const string View = "BazaarPlusPlus.Android.LiveBuild.AndroidLiveBuildView";
    private const string Inspection = "BazaarPlusPlus.Android.LiveBuild.AndroidLiveBuildInspection";
    private const string Collection = "BazaarPlusPlus.Android.Collection.AndroidCollectionPanel";

    private static readonly Lazy<CompiledAssembly> Host = new(() =>
        CompiledAssembly.Load(
            typeof(AndroidSceneChangeTeardownTests)
                .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
                .Value!
        )
    );

    [Theory]
    [InlineData(Panel)]
    [InlineData(Collection)]
    public void A_scene_change_closes_the_panel_with_the_scene_changed_reason(string panel)
    {
        var closes = Type(TouchUi)
            .MethodClosure("OnSceneLoaded")
            .Where(method => method.Instructions.Any(op => op.Targets(panel, "Close")))
            .ToArray();

        Assert.NotEmpty(closes);
        Assert.All(closes, method => Assert.Contains("scene_changed", method.Literals));
    }

    [Fact]
    public void Closing_the_ten_win_builds_panel_closes_its_inspected_card()
    {
        Assert.True(
            IndexOf(Method(Panel, "Close").Instructions, View, "ReturnAll") >= 0,
            "AndroidLiveBuildPanel.Close must return the view's cards"
        );
        Assert.True(
            IndexOf(Method(View, "ReturnAll").Instructions, Inspection, "Close") >= 0,
            "AndroidLiveBuildView.ReturnAll must close the inspected card"
        );
    }

    private static CompiledType Type(string fullName) =>
        Host.Value.Types.Single(type => type.FullName == fullName);

    private static CompiledMethod Method(string type, string name) =>
        Type(type).Methods.Single(method => method.Name == name && method.DeclaringType == type);

    private static int IndexOf(
        IReadOnlyList<IlInstruction> instructions,
        string type,
        string member
    )
    {
        for (var index = 0; index < instructions.Count; index++)
            if (instructions[index].Targets(type, member))
                return index;
        return -1;
    }
}
