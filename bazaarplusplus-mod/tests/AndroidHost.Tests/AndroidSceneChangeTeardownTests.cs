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
    private const string History = "BazaarPlusPlus.Android.Ui.AndroidHistoryPanel";
    private const string CombatProbe = "BazaarPlusPlus.Android.GameInterop.AndroidCombatProbe";
    private const string Badges = "BazaarPlusPlus.Android.Ui.AndroidHistoryBadges";

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

    // History stays open across a scene change, as the desktop's OnlyWhenInCombat policy keeps
    // it, and closes only into a combat. The emulator never loads a scene into a combat (combat
    // loads none), so the compiled branch is held here: the cards go back first with the
    // scene_changed reason, then the combat probe decides the scene_combat close.
    [Fact]
    public void A_scene_change_returns_historys_cards_and_closes_it_only_into_a_combat()
    {
        var instructions = Method(History, "OnSceneLoaded").Instructions;
        var returned = IndexOf(instructions, History, "ReturnCards");
        var probe = IndexOf(instructions, CombatProbe, "IsInCombat");
        var close = IndexOf(instructions, History, "Close");

        Assert.True(returned >= 0, "OnSceneLoaded must return History's cards");
        Assert.True(probe > returned, "the combat probe runs after the cards go back");
        Assert.True(close > probe, "the close follows the combat probe");
        Assert.Contains("scene_changed", Method(History, "OnSceneLoaded").Literals);
        Assert.Contains("scene_combat:", Method(History, "OnSceneLoaded").Literals);
    }

    // A non-additive scene load unloads assets no live object references; History's badge
    // atlases are runtime textures, so both the texture and each sprite opt out of that unload
    // (a replay's round trip otherwise left the run rows on their "?" fallback).
    [Fact]
    public void Historys_badge_artwork_survives_a_scene_loads_asset_unload()
    {
        var hideFlags = Method(Badges, "LoadAtlas")
            .Instructions.Count(op => op.Targets("UnityEngine.Object", "set_hideFlags"));

        Assert.Equal(2, hideFlags);
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
