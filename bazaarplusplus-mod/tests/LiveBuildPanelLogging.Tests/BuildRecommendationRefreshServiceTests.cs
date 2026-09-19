#nullable enable
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using Xunit;

namespace LiveBuildPanelLogging.Tests;

public sealed class BuildRecommendationRefreshServiceTests
{
    [Fact]
    public async Task First_success_is_updated_and_second_success_is_no_change()
    {
        var calls = 0;
        var repository = new BuildRecommendationRepository(() =>
        {
            calls++;
            return Task.FromResult(BuildRecommendationRemoteRefreshResult.Success());
        });
        var service = new BuildRecommendationRefreshService();

        var first = await service.RefreshAsync(repository, CancellationToken.None);
        var second = await service.RefreshAsync(repository, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(BuildRecommendationRefreshOutcome.Updated, first.Outcome);
        Assert.Equal(BuildRecommendationRefreshOutcome.NoChange, second.Outcome);
    }

    [Fact]
    public async Task Failure_preserves_normalized_reason_exception_and_display_error()
    {
        var exception = new InvalidOperationException("remote detail");
        var repository = new BuildRecommendationRepository(() =>
            Task.FromResult(
                BuildRecommendationRemoteRefreshResult.Failure(
                    LiveBuildRefreshFailureReasonCode.RemoteRequestFailed,
                    "remote detail",
                    exception
                )
            )
        );
        var service = new BuildRecommendationRefreshService();

        var result = await service.RefreshAsync(repository, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(BuildRecommendationRefreshOutcome.Failed, result.Outcome);
        Assert.Equal(LiveBuildRefreshFailureReasonCode.RemoteRequestFailed, result.FailureReason);
        Assert.Equal("remote detail", result.Error);
        Assert.Same(exception, result.Exception);
    }
}
