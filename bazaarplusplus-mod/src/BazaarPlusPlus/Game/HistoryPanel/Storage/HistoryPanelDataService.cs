#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.HistoryPanel.Storage;

internal sealed class HistoryPanelDataService
{
    private readonly HistoryPanelRepository? _repository;
    private readonly GhostBattleSyncService? _ghostSyncService;
    private readonly Func<string?>? _replayDirectoryPathAccessor;

    public HistoryPanelDataService(
        HistoryPanelRepository? repository,
        GhostBattleSyncService? ghostSyncService = null
    )
        : this(repository, ghostSyncService, replayDirectoryPathAccessor: null) { }

    public HistoryPanelDataService(
        HistoryPanelRepository? repository,
        GhostBattleSyncService? ghostSyncService,
        Func<string?>? replayDirectoryPathAccessor = null
    )
    {
        _repository = repository;
        _ghostSyncService = ghostSyncService;
        _replayDirectoryPathAccessor = replayDirectoryPathAccessor;
    }

    public bool IsAvailable => _repository != null;

    public bool DatabaseExists => _repository?.DatabaseExists ?? false;

    public bool CanSyncGhostBattles => _ghostSyncService != null;

    public bool TryLoadRecentRuns(
        int limit,
        out IReadOnlyList<HistoryRunRecord> runs,
        out string statusMessage,
        out Exception? error
    )
    {
        runs = Array.Empty<HistoryRunRecord>();
        error = null;

        if (_repository == null)
        {
            statusMessage = HistoryPanelText.RunLogDatabasePathUnavailable();
            return false;
        }

        try
        {
            runs = _repository.ListRecentRuns(limit);
            statusMessage = _repository.DatabaseExists
                ? HistoryPanelText.LoadedRuns(runs.Count)
                : HistoryPanelText.DatabaseFileMissing();
            return true;
        }
        catch (Exception ex)
        {
            statusMessage = HistoryPanelText.HistoryLoadFailed(ex.Message);
            error = ex;
            return false;
        }
    }

    public bool TryLoadBattles(
        string? runId,
        out IReadOnlyList<HistoryBattleRecord> battles,
        out Exception? error
    )
    {
        battles = Array.Empty<HistoryBattleRecord>();
        error = null;

        if (_repository == null || string.IsNullOrWhiteSpace(runId))
            return true;

        try
        {
            battles = _repository.ListBattlesByRun(runId);
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    public bool TryDeleteRun(
        string runId,
        out IReadOnlyList<string> battleIds,
        out Exception? error
    )
    {
        battleIds = Array.Empty<string>();
        error = null;

        if (_repository == null)
        {
            error = new InvalidOperationException(HistoryPanelText.RunLogRepositoryUnavailable());
            return false;
        }

        if (string.IsNullOrWhiteSpace(runId))
            return true;

        try
        {
            battleIds = _repository.ListBattleIdsByRun(runId);
            _repository.DeleteRun(runId);
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    public bool TryLoadGhostBattles(
        int limit,
        out IReadOnlyList<HistoryBattleRecord> battles,
        out string statusMessage,
        out Exception? error
    )
    {
        battles = Array.Empty<HistoryBattleRecord>();
        error = null;

        if (_repository == null)
        {
            statusMessage = HistoryPanelText.RunLogDatabasePathUnavailable();
            return false;
        }

        try
        {
            battles = _repository.ListRecentGhostBattles(limit);
            if (BackfillDownloadedGhostBattleCounts(battles))
                battles = _repository.ListRecentGhostBattles(limit);
            statusMessage = HistoryPanelText.LoadedGhostBattles(battles.Count);
            return true;
        }
        catch (Exception ex)
        {
            statusMessage = HistoryPanelText.GhostHistoryLoadFailed(ex.Message);
            error = ex;
            return false;
        }
    }

    private bool BackfillDownloadedGhostBattleCounts(IReadOnlyList<HistoryBattleRecord> battles)
    {
        if (_repository == null || battles.Count == 0)
            return false;

        var replayDirectoryPath = _replayDirectoryPathAccessor?.Invoke();
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return false;

        GhostBattlePayloadStore? payloadStore = null;
        var updated = false;
        foreach (var battle in battles)
        {
            if (
                battle.Source != HistoryBattleSource.Ghost
                || !battle.ReplayDownloaded
                || battle.SnapshotCounts.HasAnyRecordedCard
            )
                continue;

            payloadStore ??= new GhostBattlePayloadStore(
                GhostBattlePayloadStore.ResolveDirectory(replayDirectoryPath)
            );
            var loadResult = payloadStore.LoadDetailed(battle.BattleId);
            if (loadResult.Status != FileBackedPayloadLoadStatus.Loaded)
                continue;

            var snapshots = loadResult.Payload?.BattleManifest?.Snapshots;
            if (snapshots == null)
                continue;

            var counts = HistoryBattlePreviewProjection.CountSnapshots(
                snapshots.PlayerHand,
                snapshots.PlayerSkills,
                snapshots.OpponentHand,
                snapshots.OpponentSkills
            );
            if (!counts.HasAnyRecordedCard)
                continue;

            _repository.MarkGhostReplayDownloaded(battle.BattleId, counts);
            updated = true;
        }

        return updated;
    }

    public async Task<HistoryPanelAttemptResult> SyncGhostBattlesAsync(
        CancellationToken cancellationToken
    )
    {
        if (_ghostSyncService == null)
            return HistoryPanelAttemptResult.Failure(
                HistoryPanelText.GhostSyncUnavailable(),
                HistoryPanelGhostSyncReasonCode.SyncUnavailable
            );

        try
        {
            var result = await _ghostSyncService.SyncRecentBattlesAsync(cancellationToken);
            if (!result.Succeeded)
                return HistoryPanelAttemptResult.Failure(
                    HistoryPanelText.GhostSyncFailed(result.Error ?? HistoryPanelText.Unknown()),
                    result.ReasonCode,
                    result.Exception
                );

            return HistoryPanelAttemptResult.Success(
                HistoryPanelText.GhostSyncSucceeded(result.ImportedCount),
                result.ImportedCount
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HistoryPanelAttemptResult.Failure(
                HistoryPanelText.GhostSyncFailed(ex.Message),
                HistoryPanelGhostSyncReasonCode.UnexpectedException,
                ex
            );
        }
    }
}

internal readonly struct HistoryPanelAttemptResult
{
    private HistoryPanelAttemptResult(
        bool succeeded,
        string statusMessage,
        int importedCount,
        HistoryPanelGhostSyncReasonCode reasonCode,
        Exception? error
    )
    {
        Succeeded = succeeded;
        StatusMessage = statusMessage;
        ImportedCount = importedCount;
        ReasonCode = reasonCode;
        Error = error;
    }

    public bool Succeeded { get; }

    public string StatusMessage { get; }
    public int ImportedCount { get; }
    public HistoryPanelGhostSyncReasonCode ReasonCode { get; }

    public Exception? Error { get; }

    public static HistoryPanelAttemptResult Success(string statusMessage, int importedCount) =>
        new(true, statusMessage, importedCount, HistoryPanelGhostSyncReasonCode.Completed, null);

    public static HistoryPanelAttemptResult Failure(
        string statusMessage,
        HistoryPanelGhostSyncReasonCode reasonCode,
        Exception? error = null
    ) => new(false, statusMessage, 0, reasonCode, error);
}
