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

    public HistoryPage<HistoryRunRecord> LoadRuns(HistoryPageRequest request, string? hero) =>
        _repository?.ListRuns(request, hero) ?? HistoryPage<HistoryRunRecord>.Empty;

    public HistoryPage<HistoryBattleRecord> LoadBattles(string runId, HistoryPageRequest request) =>
        _repository?.ListBattles(runId, request) ?? HistoryPage<HistoryBattleRecord>.Empty;

    public HistoryPage<HistoryBattleRecord> LoadGhosts(
        string account,
        GhostBattleFilter filter,
        bool dayMin10,
        HistoryPageRequest request
    ) =>
        _repository?.ListGhostBattles(account, filter, dayMin10, request)
        ?? HistoryPage<HistoryBattleRecord>.Empty;

    public BazaarPlusPlus.Game.PvpBattles.PvpBattleSnapshots? LoadDetail(
        HistoryBattleRecord battle,
        string account
    )
    {
        if (_repository == null)
            return null;
        if (battle.Source == HistoryBattleSource.Local)
            return _repository.LoadSnapshots(battle.RunId, battle.BattleId);
        var reference = _repository.TryGetGhostBundleReference(battle.BattleId);
        if (reference == null || reference.LocalPlayerAccountId != account)
            return null;
        var path = _replayDirectoryPathAccessor?.Invoke();
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var store = new GhostBattlePayloadStore(GhostBattlePayloadStore.ResolveDirectory(path));
        var result = store.LoadDetailed(battle.BattleId);
        if (
            result.Status
            is FileBackedPayloadLoadStatus.Invalid
                or FileBackedPayloadLoadStatus.Unreadable
        )
            throw result.Exception
                ?? new InvalidDataException("Ghost payload is invalid or exceeds its size limit.");
        var payload = GhostBattlePayloadReader.Normalize(result.Payload);
        if (payload == null)
            return null;
        if (
            !GhostBattlePayloadReader.MatchesIdentity(
                payload,
                battle.BattleId,
                account,
                reference.UploaderAccountId
            )
        )
            throw new InvalidDataException(
                "Ghost payload identity does not match this account and battle."
            );
        var snapshots = payload!.BattleManifest.Snapshots;
        if (!battle.SnapshotCounts.Known)
            _repository.MarkGhostReplayDownloaded(
                battle.BattleId,
                new HistoryBattleSnapshotCounts(
                    snapshots.PlayerHand.Items.Count,
                    snapshots.PlayerSkills.Items.Count,
                    snapshots.OpponentHand.Items.Count,
                    snapshots.OpponentSkills.Items.Count
                )
            );
        return snapshots;
    }

    internal int MaintainGhosts(string account, CancellationToken cancellationToken)
    {
        if (
            _repository == null
            || !_repository.DatabaseExists
            || string.IsNullOrWhiteSpace(account)
        )
            return 0;
        _repository.MarkOldUndownloadedGhostBattlesDeleted(DateTimeOffset.UtcNow);
        var directory = _replayDirectoryPathAccessor?.Invoke();
        if (string.IsNullOrWhiteSpace(directory))
            return 0;
        var store = new GhostBattlePayloadStore(
            GhostBattlePayloadStore.ResolveDirectory(directory)
        );
        HistoryCursor? cursor = null;
        var restored = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = _repository.ListHiddenGhosts(account, cursor);
            foreach (var candidate in page.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = store.LoadDetailed(candidate.Cursor.Id);
                var payload = GhostBattlePayloadReader.Normalize(result.Payload);
                if (
                    result.Status == FileBackedPayloadLoadStatus.Loaded
                    && GhostBattlePayloadReader.MatchesIdentity(
                        payload,
                        candidate.Cursor.Id,
                        account,
                        candidate.Uploader
                    )
                    && payload!.ReplayPayload
                        is { SpawnMessageBytes.Length: > 0, CombatMessageBytes.Length: > 0 }
                    && _repository.RestoreHiddenGhost(candidate)
                )
                    restored++;
            }
            if (!page.HasOlder)
                return restored;
            cursor = page.Last;
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
            battleIds = _repository.DeleteRun(runId).BattleIds;
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
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
                HistoryPanelText.GhostSyncSucceeded(
                    result.ImportedCount,
                    result.DiscoveryLimitReached
                ),
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
