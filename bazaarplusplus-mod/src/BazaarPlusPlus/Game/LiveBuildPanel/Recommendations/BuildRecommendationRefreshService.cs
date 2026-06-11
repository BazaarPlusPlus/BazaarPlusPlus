#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

/// <summary>
/// Shared manual-refresh entry over the ten-win build corpus. Awaits the repository's async
/// remote refresh so panel callers stay off the Unity main thread while the download is in
/// flight. The token only prevents the refresh from starting (the HTTP read is not interruptible
/// once issued); callers guard their own stale continuations.
/// </summary>
internal sealed class BuildRecommendationRefreshService
{
    public async Task<BuildRecommendationRefreshResult> RefreshAsync(
        BuildRecommendationRepository repository,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var (succeeded, error) = await repository
                .TryRefreshFinalBuildsFromRemoteAsync()
                .ConfigureAwait(false);
            return succeeded
                ? BuildRecommendationRefreshResult.Success()
                : BuildRecommendationRefreshResult.Failure(error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BuildRecommendationRefreshResult.Failure(ex.Message);
        }
    }
}

internal readonly struct BuildRecommendationRefreshResult
{
    private BuildRecommendationRefreshResult(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public static BuildRecommendationRefreshResult Success() => new(true, null);

    public static BuildRecommendationRefreshResult Failure(string? error) => new(false, error);
}
