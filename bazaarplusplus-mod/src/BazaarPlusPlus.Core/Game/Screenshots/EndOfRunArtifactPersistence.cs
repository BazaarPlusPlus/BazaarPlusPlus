#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunScreenshot;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Game.Screenshots;

public sealed class EndOfRunArtifactPersistence : IEndOfRunArtifactPersistence
{
    private readonly RunScreenshotSqliteStore? _store;

    // Required, as for every Core store user: the Android host must pass its DELETE mode, and
    // null keeps the store's default (the desktop's WAL).
    public EndOfRunArtifactPersistence(IBppServices services, SqliteOpenOptions? databaseOptions)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        _store = new RunScreenshotSqliteStore(
            PathConstants.RunLogDatabase(services.Paths.RequireDataRoot()),
            databaseOptions
        );
    }

    public Task<ScreenshotMetadataPersistenceOutcome> PersistAsync(
        ScreenshotCaptureResult capture,
        bool isPrimary
    )
    {
        if (capture == null || _store == null)
            return Task.FromResult(ScreenshotMetadataPersistenceOutcome.Unavailable());

        var record = RunScreenshotRecordMapper.CreateRecord(capture, isPrimary);
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
