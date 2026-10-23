using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Localization;
using LiveBuildPanel.Tests;

L.Install(() => "en", () => BppChineseLocaleMode.Mainland);

TestCandidateToggleUsesTemplateId();
TestCandidatePruneKeepsSelectableRowsOnly();
TestRowVmTogglePolicyComesFromBoardType();
TestNoRunRowsSuppressEmptyText();
TestActiveRunRowsKeepSpecificEmptyStates();
TestRowsKeepFourPeerOrder();
TestCandidateIdentityIsSharedAcrossSources();
TestCorpusFreshnessKeepsLocalizedRelativeTime();

L.Install(() => "en", () => BppChineseLocaleMode.Mainland);
LiveBuildRefreshStateTests.Run();
LiveBuildPanelStateTests.Run();
LiveBuildTouchLayoutTests.Run();

Console.WriteLine("LiveBuildPanel checks passed.");

static void TestCandidateToggleUsesTemplateId()
{
    var state = new LiveBuildCandidateState();
    var templateId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    Check.That(state.Toggle(templateId), "First toggle should change state.");
    Check.That(state.Contains(templateId), "Template id should be selected after first toggle.");
    Check.That(state.Toggle(templateId), "Second toggle should change state.");
    Check.That(!state.Contains(templateId), "Template id should be removed after second toggle.");
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

    Check.That(
        state.Contains(selectableTemplate),
        "Selectable row candidate should survive prune."
    );
    Check.That(!state.Contains(referenceTemplate), "Reference row candidate should be pruned.");
}

static void TestRowVmTogglePolicyComesFromBoardType()
{
    Check.That(
        new LiveItemBoardRowVm(
            Board(BppItemBoardId.FinalBuild, BppItemBoardType.Reference, Guid.NewGuid()),
            "final",
            "empty"
        ).CanToggleCandidates == false,
        "Final build row should not expose candidate hit targets."
    );
    Check.That(
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
        Check.That(
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
            Hero = CardHero.Vanessa,
            Shop = EmptyBoard(BppItemBoardId.LiveShop, BppItemBoardType.SelectableShop),
            Stash = EmptyBoard(BppItemBoardId.LiveStash, BppItemBoardType.SelectableContainer),
            Board = EmptyBoard(BppItemBoardId.LiveBoard, BppItemBoardType.SelectableContainer),
        }
    );
    Check.That(
        rows[BppItemBoardId.LiveShop].EmptyText == LiveBuildPanelText.EmptyShop(),
        "Shop empty state must remain specific."
    );
    Check.That(
        rows[BppItemBoardId.LiveStash].EmptyText == LiveBuildPanelText.EmptyStash(),
        "Stash empty state must remain specific."
    );
    Check.That(
        rows[BppItemBoardId.LiveBoard].EmptyText == LiveBuildPanelText.EmptyBoard(),
        "Board empty state must remain specific."
    );
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
    Check.That(
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
    Check.That(
        state.TemplateIds.Count == 1 && state.Contains(template),
        "The same template across sources is one candidate."
    );
    state.Toggle(stash.Cards[0].TemplateId);
    Check.That(!state.HasCandidates, "Toggling another source clears the same candidate.");
}

static void TestCorpusFreshnessKeepsLocalizedRelativeTime()
{
    L.Install(() => "zh-CN", () => BppChineseLocaleMode.Mainland);
    var summary = new TenWinCorpusSummary(
        new DateTimeOffset(2034, 5, 16, 5, 28, 9, TimeSpan.Zero),
        1240,
        7
    );
    var freshness = LiveBuildPanelText.CorpusFreshnessLine(
        summary,
        new DateTimeOffset(2034, 5, 16, 7, 28, 9, TimeSpan.Zero)
    );
    Check.That(
        freshness.Contains("2 小时前", StringComparison.Ordinal),
        "Corpus freshness should retain the localized relative update time."
    );
}

static BppItemBoard Board(BppItemBoardId id, BppItemBoardType type, Guid templateId) =>
    new(id, type, [new BppItemBoardCard { TemplateId = templateId, Size = CardSize.Small }]);

static BppItemBoard EmptyBoard(BppItemBoardId id, BppItemBoardType type) =>
    new(id, type, Array.Empty<BppItemBoardCard>());

static Dictionary<BppItemBoardId, LiveItemBoardRowVm> RowsById(LiveBuildPanelSnapshot snapshot) =>
    snapshot.Rows.ToDictionary(row => row.Board.Id);
