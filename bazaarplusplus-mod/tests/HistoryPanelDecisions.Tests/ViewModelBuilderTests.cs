#nullable enable
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.Supporters;

// The view model both hosts render. Without a coordinator the panel shows itself unavailable;
// the board layout and the delete action follow the section (ADR-0002: Ghost shows only the
// challenger's board).
internal static class ViewModelBuilderTests
{
    public static void Run()
    {
        WithoutACoordinatorThePanelIsUnavailableAndNothingActs();
        GhostShowsOneBoardAndNoDelete();
        TheHostsSupporterSamplesPassThrough();
    }

    private static void WithoutACoordinatorThePanelIsUnavailableAndNothingActs()
    {
        var model = Build(new HistoryPanelState());

        Require(!model.ReplayButtonEnabled, "No coordinator must leave replay disabled.");
        Require(!model.RecordAndReplayButtonEnabled, "No coordinator must leave record disabled.");
        Require(!model.DeleteButtonEnabled, "No coordinator must leave delete disabled.");
        Require(!model.FavoritesTabVisible, "No coordinator means no Favorites tab.");
        Require(model.Runs.Count == 0 && model.VisibleBattles.Count == 0, "No rows without one.");
        Require(model.Title == HistoryPanelText.Title(), "The title is History's.");
        Require(
            HistoryPanelViewModelBuilder.ActiveSelectedBattle(new HistoryPanelState(), null)
                == null,
            "No battle is selected without rows."
        );
    }

    private static void GhostShowsOneBoardAndNoDelete()
    {
        var runs = new HistoryPanelState { SectionMode = HistorySectionMode.Runs };
        var ghost = new HistoryPanelState { SectionMode = HistorySectionMode.Ghost };

        Require(HistoryPanelViewModelBuilder.ShowsBothBoards(runs), "Runs shows both boards.");
        Require(
            !HistoryPanelViewModelBuilder.ShowsBothBoards(ghost),
            "Ghost shows only the challenger's board."
        );
        var runsModel = Build(runs);
        var ghostModel = Build(ghost);
        Require(runsModel.ShowsBothBoards && runsModel.DeleteButtonVisible, "Runs keeps delete.");
        Require(
            !ghostModel.ShowsBothBoards && !ghostModel.DeleteButtonVisible,
            "Ghost hides delete and the opponent board."
        );
        Require(ghostModel.SectionMode == HistorySectionMode.Ghost, "The section passes through.");
    }

    private static void TheHostsSupporterSamplesPassThrough()
    {
        var supporters = new List<BPPSupporterSample>();
        Require(
            ReferenceEquals(
                HistoryPanelViewModelBuilder
                    .Build(
                        new HistoryPanelState(),
                        null,
                        null,
                        null,
                        0f,
                        supporters,
                        default,
                        default
                    )
                    .Supporters,
                supporters
            ),
            "The host's supporter samples render as given."
        );
    }

    private static HistoryPanelViewModel Build(HistoryPanelState state) =>
        HistoryPanelViewModelBuilder.Build(
            state,
            coordinator: null,
            runContext: null,
            accountLinkGate: null,
            now: 0f,
            supporters: Array.Empty<BPPSupporterSample>(),
            playerBoard: default,
            opponentBoard: default
        );

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
