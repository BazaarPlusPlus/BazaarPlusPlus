#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Storage.RunScreenshot;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunArtifactPersistence : IEndOfRunArtifactPersistence
{
    private readonly IBppServices _services;
    private readonly RunScreenshotSqliteStore? _store;

    internal EndOfRunArtifactPersistence(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        if (!string.IsNullOrWhiteSpace(services.Paths.RunLogDatabasePath))
            _store = new RunScreenshotSqliteStore(services.Paths.RunLogDatabasePath);
    }

    public Task<ScreenshotMetadataPersistenceOutcome> PersistAsync(
        ScreenshotCaptureResult capture,
        bool isPrimary
    )
    {
        if (capture == null || _store == null)
            return Task.FromResult(ScreenshotMetadataPersistenceOutcome.Unavailable());

        var probe = _services.RunSnapshot;
        var basics = probe.TryGetRunBasics(out var basicsSnapshot) ? basicsSnapshot : null;
        var rank = probe.TryGetRankSnapshot(out var rankSnapshot) ? rankSnapshot : null;
        var position = probe.TryGetLeaderboardPosition(out var leaderboardPosition)
            ? leaderboardPosition
            : null;
        var record = RunScreenshotRecordMapper.CreateRecord(
            capture,
            basics,
            rank,
            position,
            isPrimary,
            _services.GameBuild.Channel.ToString()
        );
        var store = _store;

        return Task.Run(() =>
        {
            try
            {
                store.Save(record);
                return ScreenshotMetadataPersistenceOutcome.Saved();
            }
            catch (Exception ex)
            {
                return ScreenshotMetadataPersistenceOutcome.Failed(ex);
            }
        });
    }
}
