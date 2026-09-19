#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunScreenshot;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunArtifactPersistence : IEndOfRunArtifactPersistence
{
    private readonly string _buildChannel;
    private readonly RunScreenshotSqliteStore? _store;

    internal EndOfRunArtifactPersistence(IBppServices services)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        _buildChannel = services.GameBuild.Channel.ToString();
        _store = new RunScreenshotSqliteStore(
            PathConstants.RunLogDatabase(services.Paths.RequireDataRoot())
        );
    }

    public Task<ScreenshotMetadataPersistenceOutcome> PersistAsync(
        ScreenshotCaptureResult capture,
        bool isPrimary
    )
    {
        if (capture == null || _store == null)
            return Task.FromResult(ScreenshotMetadataPersistenceOutcome.Unavailable());

        var record = RunScreenshotRecordMapper.CreateRecord(capture, isPrimary, _buildChannel);
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
