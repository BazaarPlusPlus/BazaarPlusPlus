using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Localization;
using Xunit;

namespace LiveBuildPanel.Tests;

public sealed class LiveBuildRefreshStateTests
{
    // The capsule installed English before these checks.
    public LiveBuildRefreshStateTests() =>
        L.Install(() => "en", () => BppChineseLocaleMode.Mainland);

    [Fact]
    public void HiddenAndPendingPanelsIgnoreRefreshIntents()
    {
        var state = new LiveBuildRefreshState();
        Check.Equal(LiveBuildRefreshRequest.Ignore, state.BeginRefresh(), "Hidden refresh.");
        state.SetVisible(true);
        Check.Equal(LiveBuildRefreshRequest.Download, state.BeginRefresh(), "First refresh.");
        Check.That(state.IsPending, "A started refresh is pending.");
        Check.Equal(LiveBuildRefreshRequest.Ignore, state.BeginRefresh(), "Pending refresh.");
    }

    [Fact]
    public void FailurePreservesDiagnosticsAndAllowsAnotherDownload()
    {
        var state = OpenPanel();
        state.BeginRefresh();
        var exception = new InvalidOperationException("remote detail");

        var effect = state.CompleteRefresh(
            BuildRecommendationRefreshResult.Failure(
                LiveBuildRefreshFailureReasonCode.RemoteRequestFailed,
                "remote detail",
                exception
            )
        );

        Check.Equal(LiveBuildRefreshEffect.Status, effect, "A failure redraws status only.");
        Check.That(!state.IsPending, "A completed refresh is not pending.");
        var feedback = state.Feedback!.Value;
        Check.Equal(
            LiveBuildRefreshFailureReasonCode.RemoteRequestFailed,
            feedback.FailureReason,
            "The failure keeps its reason."
        );
        Check.Equal("remote detail", feedback.Error, "The failure keeps its detail.");
        Check.That(ReferenceEquals(exception, feedback.Exception), "The failure keeps its cause.");
        Check.Equal(
            LiveBuildRefreshRequest.Download,
            state.BeginRefresh(),
            "A failure allows another download."
        );
        Check.That(state.Feedback == null, "A new refresh clears the old feedback.");
    }

    [Fact]
    public void SuccessfulPullConsumesTheAllowanceAcrossPanelReopens()
    {
        var state = OpenPanel();
        Check.Equal(LiveBuildRefreshRequest.Download, state.BeginRefresh(), "First pull.");
        Check.Equal(
            LiveBuildRefreshEffect.Recommendations,
            state.CompleteRefresh(BuildRecommendationRefreshResult.Updated()),
            "A success redraws recommendations."
        );
        state.SetVisible(false);
        state.SetVisible(true);
        Check.That(state.Feedback == null, "Reopening clears the feedback.");

        Check.Equal(LiveBuildRefreshRequest.NoChange, state.BeginRefresh(), "Second pull.");
        Check.Equal(
            LiveBuildRefreshEffect.Recommendations,
            state.CompleteRefresh(BuildRecommendationRefreshResult.NoChange()),
            "A no-change success redraws recommendations."
        );
        Check.Equal(
            BuildRecommendationRefreshOutcome.NoChange,
            state.Feedback!.Value.Outcome,
            "The second pull reports no change."
        );
    }

    [Fact]
    public void ReopeningAPendingRefreshKeepsItAndPresentsItsCompletion()
    {
        var state = OpenPanel();
        state.BeginRefresh();
        state.SetVisible(false);
        Check.That(state.IsPending, "Hiding keeps the refresh pending.");
        state.SetVisible(true);

        Check.That(state.IsPending, "Reopening keeps the refresh pending.");
        Check.Equal(LiveBuildRefreshRequest.Ignore, state.BeginRefresh(), "Pending refresh.");
        Check.Equal(
            LiveBuildRefreshEffect.Recommendations,
            state.CompleteRefresh(BuildRecommendationRefreshResult.Updated()),
            "The reopened panel presents the completion."
        );
        Check.That(state.Feedback!.Value.Succeeded, "The completion succeeded.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HiddenCompletionHasNoViewEffectAndReopenClearsFeedback(bool succeeded)
    {
        var state = OpenPanel();
        state.BeginRefresh();
        state.SetVisible(false);
        var result = succeeded
            ? BuildRecommendationRefreshResult.Updated()
            : BuildRecommendationRefreshResult.Failure(
                LiveBuildRefreshFailureReasonCode.RefreshException,
                null
            );

        Check.Equal(
            LiveBuildRefreshEffect.None,
            state.CompleteRefresh(result),
            "A hidden completion has no view effect."
        );
        Check.That(!state.IsPending, "A hidden completion is not pending.");
        Check.That(state.Feedback != null, "A hidden completion keeps its feedback.");
        state.SetVisible(true);
        Check.That(state.Feedback == null, "Reopening clears the feedback.");
        Check.Equal(
            succeeded ? LiveBuildRefreshRequest.NoChange : LiveBuildRefreshRequest.Download,
            state.BeginRefresh(),
            "Only a success consumes the download."
        );
    }

    [Fact]
    public void DestroyedPanelNeverResumesViewEffects()
    {
        var state = OpenPanel();
        state.BeginRefresh();
        state.Destroy();
        state.SetVisible(true);

        Check.That(!state.IsVisible, "A destroyed panel never becomes visible.");
        Check.Equal(
            LiveBuildRefreshEffect.None,
            state.CompleteRefresh(BuildRecommendationRefreshResult.Updated()),
            "A destroyed panel has no view effect."
        );
        Check.Equal(LiveBuildRefreshRequest.Ignore, state.BeginRefresh(), "Destroyed refresh.");
    }

    private static LiveBuildRefreshState OpenPanel()
    {
        var state = new LiveBuildRefreshState();
        state.SetVisible(true);
        return state;
    }
}
