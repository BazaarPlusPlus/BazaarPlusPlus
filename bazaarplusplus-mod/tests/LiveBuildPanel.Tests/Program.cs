using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.Infrastructure.UiTokens;
using BazaarPlusPlus.Localization;

L.Install(
    new FixedLanguageProvider("en"),
    new FixedLocaleModeProvider(BppChineseLocaleMode.Mainland)
);

TestOverlaySortingLayersKeepNativeCardsBetweenPanelAndForeground();
TestSupporterAttributionCountFillsRail();
TestCandidateToggleUsesTemplateId();
TestCandidatePruneKeepsSelectableRowsOnly();
TestRowVmTogglePolicyComesFromBoardType();
TestNoRunRowsSuppressEmptyText();
TestActiveRunRowsKeepSpecificEmptyStates();

TestRowsKeepFourPeerOrder();
TestCandidateIdentityIsSharedAcrossSources();
TestCorpusFreshnessKeepsLocalizedRelativeTime();

Console.WriteLine("LiveBuildPanel checks passed.");

static void TestOverlaySortingLayersKeepNativeCardsBetweenPanelAndForeground()
{
    Assert(
        BppOverlaySorting.PanelUiToolkit < BppOverlaySorting.NativeCardPreview,
        "Native cards must render above the main UI Toolkit panel."
    );
    Assert(
        BppOverlaySorting.NativeCardPreview < BppOverlaySorting.PanelForeground,
        "Foreground markers must render above native cards."
    );
}

static void TestSupporterAttributionCountFillsRail()
{
    RegisterPluginReflectionAssemblyResolution();
    var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "BazaarPlusPlus.dll"));
    var panelType = assembly.GetType("BazaarPlusPlus.Game.LiveBuildPanel.LiveBuildPanel");
    Assert(
        panelType != null,
        $"Plugin assembly should contain LiveBuildPanel: {assembly.FullName}."
    );

    var field = panelType!.GetField(
        "SupporterAttributionCount",
        BindingFlags.NonPublic | BindingFlags.Static
    );
    Assert(field != null, "LiveBuildPanel should keep supporter attribution count named.");
    var count = (int)field!.GetRawConstantValue()!;
    Assert(
        count == 4,
        $"LiveBuildPanel should fill the four-supporter attribution rail, got {count}."
    );
}

static void RegisterPluginReflectionAssemblyResolution()
{
    AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
    {
        var name = new AssemblyName(args.Name).Name;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (var root in ManagedAssemblySearchRoots())
        {
            var candidate = Path.Combine(root, $"{name}.dll");
            if (File.Exists(candidate))
                return Assembly.LoadFrom(candidate);
        }

        return null;
    };
}

static IEnumerable<string> ManagedAssemblySearchRoots()
{
    yield return AppContext.BaseDirectory;
    yield return @"C:\Program Files (x86)\Steam\steamapps\common\The Bazaar\TheBazaar_Data\Managed";
    yield return @"C:\Program Files\Steam\steamapps\common\The Bazaar\TheBazaar_Data\Managed";
    yield return @"D:\Steam\steamapps\common\The Bazaar\TheBazaar_Data\Managed";
    yield return @"E:\Steam\steamapps\common\The Bazaar\TheBazaar_Data\Managed";
    yield return MacSteamManagedPath();
}

static string MacSteamManagedPath() =>
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library/Application Support/Steam/steamapps/common/The Bazaar/TheBazaar.app/Contents/Resources/Data/Managed"
    );

static void TestCandidateToggleUsesTemplateId()
{
    var state = new LiveBuildCandidateState();
    var templateId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    Assert(state.Toggle(templateId), "First toggle should change state.");
    Assert(state.Contains(templateId), "Template id should be selected after first toggle.");
    Assert(state.Toggle(templateId), "Second toggle should change state.");
    Assert(!state.Contains(templateId), "Template id should be removed after second toggle.");
}

static void TestCandidatePruneKeepsSelectableRowsOnly()
{
    var state = new LiveBuildCandidateState();
    var selectableTemplate = Guid.Parse("22222222-2222-2222-2222-222222222222");
    var referenceTemplate = Guid.Parse("33333333-3333-3333-3333-333333333333");
    state.Toggle(selectableTemplate);
    state.Toggle(referenceTemplate);

    state.PruneToSelectableRows([
        Board(BppItemBoardId.LiveBoard, BppItemBoardType.SelectableContainer, selectableTemplate),
        Board(BppItemBoardId.FinalBuild, BppItemBoardType.Reference, referenceTemplate),
    ]);

    Assert(state.Contains(selectableTemplate), "Selectable row candidate should survive prune.");
    Assert(!state.Contains(referenceTemplate), "Reference row candidate should be pruned.");
}

static void TestRowVmTogglePolicyComesFromBoardType()
{
    Assert(
        new LiveItemBoardRowVm(
            Board(BppItemBoardId.FinalBuild, BppItemBoardType.Reference, Guid.NewGuid()),
            "final",
            "empty"
        ).CanToggleCandidates == false,
        "Final build row should not expose candidate hit targets."
    );
    Assert(
        new LiveItemBoardRowVm(
            Board(BppItemBoardId.LiveShop, BppItemBoardType.SelectableShop, Guid.NewGuid()),
            "shop",
            "empty"
        ).CanToggleCandidates,
        "Shop row should expose candidate hit targets."
    );
}

static void TestNoRunRowsSuppressEmptyText()
{
    var rows = RowsById(
        new LiveBuildPanelSnapshot
        {
            Shop = EmptyBoard(BppItemBoardId.LiveShop, BppItemBoardType.SelectableShop),
            Board = EmptyBoard(BppItemBoardId.LiveBoard, BppItemBoardType.SelectableContainer),
            Stash = EmptyBoard(BppItemBoardId.LiveStash, BppItemBoardType.SelectableContainer),
        }
    );

    foreach (
        var id in new[]
        {
            BppItemBoardId.LiveShop,
            BppItemBoardId.LiveBoard,
            BppItemBoardId.LiveStash,
        }
    )
    {
        Assert(
            string.IsNullOrEmpty(rows[id].EmptyText),
            $"{id} no-run empty text should be suppressed."
        );
    }
}

static void TestActiveRunRowsKeepSpecificEmptyStates()
{
    var rows = RowsById(
        new LiveBuildPanelSnapshot
        {
            Hero = EHero.Vanessa,
            Shop = EmptyBoard(BppItemBoardId.LiveShop, BppItemBoardType.SelectableShop),
            Stash = EmptyBoard(BppItemBoardId.LiveStash, BppItemBoardType.SelectableContainer),
            Board = EmptyBoard(BppItemBoardId.LiveBoard, BppItemBoardType.SelectableContainer),
        }
    );
    Assert(
        rows[BppItemBoardId.LiveShop].EmptyText == LiveBuildPanelText.EmptyShop(),
        "Shop empty state must remain specific."
    );
    Assert(
        rows[BppItemBoardId.LiveStash].EmptyText == LiveBuildPanelText.EmptyStash(),
        "Stash empty state must remain specific."
    );
    Assert(
        rows[BppItemBoardId.LiveBoard].EmptyText == LiveBuildPanelText.EmptyBoard(),
        "Board empty state must remain specific."
    );
}

static BppItemBoard Board(BppItemBoardId id, BppItemBoardType type, Guid templateId) =>
    new(id, type, [new BppItemBoardCard { TemplateId = templateId, Size = ECardSize.Small }]);

static BppItemBoard EmptyBoard(BppItemBoardId id, BppItemBoardType type) =>
    new(id, type, Array.Empty<BppItemBoardCard>());

static Dictionary<BppItemBoardId, LiveItemBoardRowVm> RowsById(LiveBuildPanelSnapshot snapshot) =>
    snapshot.Rows.ToDictionary(row => row.Board.Id);

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void TestRowsKeepFourPeerOrder()
{
    var snapshot = new LiveBuildPanelSnapshot
    {
        FinalBuild = EmptyBoard(BppItemBoardId.FinalBuild, BppItemBoardType.Reference),
        Shop = EmptyBoard(BppItemBoardId.LiveShop, BppItemBoardType.SelectableShop),
        Stash = EmptyBoard(BppItemBoardId.LiveStash, BppItemBoardType.SelectableContainer),
        Board = EmptyBoard(BppItemBoardId.LiveBoard, BppItemBoardType.SelectableContainer),
    };
    Assert(
        snapshot
            .Rows.Select(row => row.Board.Id)
            .SequenceEqual(
                new[]
                {
                    BppItemBoardId.FinalBuild,
                    BppItemBoardId.LiveShop,
                    BppItemBoardId.LiveStash,
                    BppItemBoardId.LiveBoard,
                }
            ),
        "All four peers must remain in recommendation, shop, stash, board order."
    );
}

static void TestCandidateIdentityIsSharedAcrossSources()
{
    var template = Guid.NewGuid();
    var state = new LiveBuildCandidateState();
    var shop = Board(BppItemBoardId.LiveShop, BppItemBoardType.SelectableShop, template);
    var stash = Board(BppItemBoardId.LiveStash, BppItemBoardType.SelectableContainer, template);
    state.Toggle(template);
    state.PruneToSelectableRows(new[] { shop, stash });
    Assert(
        state.TemplateIds.Count == 1 && state.Contains(template),
        "The same template across sources is one candidate."
    );
    state.Toggle(stash.Cards[0].TemplateId);
    Assert(!state.HasCandidates, "Toggling another source clears the same candidate.");
}

static void TestCorpusFreshnessKeepsLocalizedRelativeTime()
{
    L.Install(
        new FixedLanguageProvider("zh-CN"),
        new FixedLocaleModeProvider(BppChineseLocaleMode.Mainland)
    );
    var summary = new TenWinCorpusSummary(
        new DateTimeOffset(2034, 5, 16, 5, 28, 9, TimeSpan.Zero),
        1240,
        7
    );
    var freshness = LiveBuildPanelText.CorpusFreshnessLine(
        summary,
        new DateTimeOffset(2034, 5, 16, 7, 28, 9, TimeSpan.Zero)
    );
    Assert(
        freshness.Contains("2 小时前", StringComparison.Ordinal),
        "Corpus freshness should retain the localized relative update time."
    );
}

internal sealed class FixedLanguageProvider(string languageCode) : ILanguageProvider
{
    public string CurrentLanguageCode => languageCode;
}

internal sealed class FixedLocaleModeProvider(BppChineseLocaleMode mode) : ILocaleModeProvider
{
    public BppChineseLocaleMode CurrentMode => mode;
}
