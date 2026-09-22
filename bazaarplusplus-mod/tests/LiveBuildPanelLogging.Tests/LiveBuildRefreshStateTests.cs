#nullable enable
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using Xunit;

namespace LiveBuildPanelLogging.Tests;

public sealed class LiveBuildRefreshStateTests
{
    [Fact]
    public void Hidden_and_pending_panels_ignore_refresh_intents()
    {
        var state = new LiveBuildRefreshState();
        Assert.Equal(LiveBuildRefreshRequest.Ignore, state.BeginRefresh());
        state.SetVisible(true);
        Assert.Equal(LiveBuildRefreshRequest.Download, state.BeginRefresh());
        Assert.True(state.IsPending);
        Assert.Equal(LiveBuildRefreshRequest.Ignore, state.BeginRefresh());
    }

    [Fact]
    public void Failure_preserves_diagnostics_and_allows_another_download()
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

        Assert.Equal(LiveBuildRefreshEffect.Status, effect);
        Assert.False(state.IsPending);
        var feedback = state.Feedback!.Value;
        Assert.Equal(LiveBuildRefreshFailureReasonCode.RemoteRequestFailed, feedback.FailureReason);
        Assert.Equal("remote detail", feedback.Error);
        Assert.Same(exception, feedback.Exception);
        Assert.Equal(LiveBuildRefreshRequest.Download, state.BeginRefresh());
        Assert.Null(state.Feedback);
    }

    [Fact]
    public void Successful_pull_consumes_the_allowance_across_panel_reopens()
    {
        var state = OpenPanel();
        Assert.Equal(LiveBuildRefreshRequest.Download, state.BeginRefresh());
        Assert.Equal(
            LiveBuildRefreshEffect.Recommendations,
            state.CompleteRefresh(BuildRecommendationRefreshResult.Updated())
        );
        state.SetVisible(false);
        state.SetVisible(true);
        Assert.Null(state.Feedback);

        Assert.Equal(LiveBuildRefreshRequest.NoChange, state.BeginRefresh());
        Assert.Equal(
            LiveBuildRefreshEffect.Recommendations,
            state.CompleteRefresh(BuildRecommendationRefreshResult.NoChange())
        );
        Assert.Equal(BuildRecommendationRefreshOutcome.NoChange, state.Feedback!.Value.Outcome);
    }

    [Fact]
    public void Reopening_a_pending_refresh_keeps_it_and_presents_its_completion()
    {
        var state = OpenPanel();
        state.BeginRefresh();
        state.SetVisible(false);
        Assert.True(state.IsPending);
        state.SetVisible(true);

        Assert.True(state.IsPending);
        Assert.Equal(LiveBuildRefreshRequest.Ignore, state.BeginRefresh());
        Assert.Equal(
            LiveBuildRefreshEffect.Recommendations,
            state.CompleteRefresh(BuildRecommendationRefreshResult.Updated())
        );
        Assert.True(state.Feedback!.Value.Succeeded);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Hidden_completion_has_no_view_effect_and_reopen_clears_feedback(bool succeeded)
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

        Assert.Equal(LiveBuildRefreshEffect.None, state.CompleteRefresh(result));
        Assert.False(state.IsPending);
        Assert.NotNull(state.Feedback);
        state.SetVisible(true);
        Assert.Null(state.Feedback);
        Assert.Equal(
            succeeded ? LiveBuildRefreshRequest.NoChange : LiveBuildRefreshRequest.Download,
            state.BeginRefresh()
        );
    }

    [Fact]
    public void Destroyed_panel_never_resumes_view_effects()
    {
        var state = OpenPanel();
        state.BeginRefresh();
        state.Destroy();
        state.SetVisible(true);

        Assert.False(state.IsVisible);
        Assert.Equal(
            LiveBuildRefreshEffect.None,
            state.CompleteRefresh(BuildRecommendationRefreshResult.Updated())
        );
        Assert.Equal(LiveBuildRefreshRequest.Ignore, state.BeginRefresh());
    }

    private static LiveBuildRefreshState OpenPanel()
    {
        var state = new LiveBuildRefreshState();
        state.SetVisible(true);
        return state;
    }
}
