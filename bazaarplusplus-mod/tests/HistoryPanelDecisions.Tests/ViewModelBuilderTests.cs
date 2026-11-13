#nullable enable
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Localization;
using Xunit;

// The view model both hosts render. Without a coordinator the panel shows itself unavailable;
// the board layout and the delete action follow the section (ADR-0002: Ghost shows only the
// challenger's board).
public sealed class ViewModelBuilderTests
{
    // The capsule ran these checks with English installed.
    public ViewModelBuilderTests() => L.Install(() => "en", () => BppChineseLocaleMode.Mainland);

    [Fact]
    public void WithoutACoordinatorThePanelIsUnavailableAndNothingActs()
    {
        var model = Build(new HistoryPanelState());

        Require(!model.ReplayButtonEnabled, "No coordinator must leave replay disabled.");
        Require(!model.RecordAndReplayButtonEnabled, "No coordinator must leave record disabled.");
        Require(!model.RecordAndReplayButtonVisible, "No coordinator offers no record action.");
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

    [Fact]
    public void GhostShowsOneBoardAndNoDelete()
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

    [Fact]
    public void TheHostsSupporterSamplesPassThrough()
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
                        default,
                        hero => hero ?? string.Empty,
                        DateTimeOffset.Now
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
            opponentBoard: default,
            heroName: hero => hero ?? string.Empty,
            today: DateTimeOffset.Now
        );

    private static void Require(bool condition, string message)
    {
        if (!condition)
            Xunit.Assert.Fail(message);
    }
}
