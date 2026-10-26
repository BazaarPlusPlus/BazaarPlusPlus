#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.HistoryPanel.Storage;

public sealed class HistoryPanelDataService
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

    public HistoryCountedPage<HistoryRunRecord> LoadRuns(
        HistoryPageRequest request,
        string? hero
    ) =>
        _repository?.ListRuns(request, RunHeroKey(hero))
        ?? HistoryCountedPage<HistoryRunRecord>.Empty();

    // The stored hero key a Runs hero filter matches: a blank filter lists every Run, and an
    // unknown hero still filters (to no Runs).
    internal static string? RunHeroKey(string? hero) =>
        string.IsNullOrWhiteSpace(hero) ? null : hero!.Trim().ToLowerInvariant();

    public HistoryCursorPage<HistoryBattleRecord> LoadBattles(
        string runId,
        HistoryPageRequest request
    ) => _repository?.ListBattles(runId, request) ?? HistoryCursorPage<HistoryBattleRecord>.Empty;

    public HistoryCountedPage<HistoryBattleRecord> LoadGhosts(
        string account,
        GhostBattleFilter filter,
        bool dayMin10,
        HistoryPageRequest request
    ) =>
        _repository?.ListGhostBattles(account, RecorderOutcome(filter), dayMin10, request)
        ?? HistoryCountedPage<HistoryBattleRecord>.Empty();

    // A Ghost was recorded by the opponent, so the local player won when its recorder lost.
    internal static int? RecorderOutcome(GhostBattleFilter filter) =>
        filter switch
        {
            GhostBattleFilter.IWon => -1,
            GhostBattleFilter.ILost => 1,
            _ => null,
        };

    internal const string InvalidGhostPayloadMessage =
        "Ghost payload is invalid or exceeds its size limit.";

    public PvpBattleSnapshots? LoadDetail(HistoryBattleRecord battle, string account)
    {
        if (_repository == null)
            return null;
        if (battle.Source == HistoryBattleSource.Local)
            return _repository.LoadSnapshots(battle.RunId, battle.BattleId) is { } documents
                ? PvpBattleSnapshotsCodec.FromDocuments(documents)
                : null;
        var reference = _repository.TryGetGhostBundleReference(battle.BattleId);
        if (reference == null || reference.LocalPlayerAccountId != account)
            return null;
        var path = _replayDirectoryPathAccessor?.Invoke();
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var store = new GhostBattlePayloadStore(GhostBattlePayloadStore.ResolveDirectory(path));
        var result = store.LoadDetailed(battle.BattleId);
        // The message reaches the History status line; a decode exception stays as the cause only.
        if (result.Status == FileBackedPayloadLoadStatus.Invalid)
            throw new InvalidDataException(InvalidGhostPayloadMessage, result.Exception);
        if (result.Status == FileBackedPayloadLoadStatus.Unreadable)
            throw result.Exception ?? new InvalidDataException(InvalidGhostPayloadMessage);
        var payload = result.Payload;
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

    // Expired counts stale undownloaded rows hidden for every account; Restored counts this
    // account's hidden downloads brought back. Either changes the Ghost page.
    internal (int Expired, int Restored) MaintainGhosts(
        string account,
        CancellationToken cancellationToken
    )
    {
        if (
            _repository == null
            || !_repository.DatabaseExists
            || string.IsNullOrWhiteSpace(account)
        )
            return (0, 0);
        var expired = _repository.MarkOldUndownloadedGhostBattlesDeleted(DateTimeOffset.UtcNow);
        var directory = _replayDirectoryPathAccessor?.Invoke();
        if (string.IsNullOrWhiteSpace(directory))
            return (expired, 0);
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
                var payload = result.Payload;
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
                return (expired, restored);
            cursor = page.Last;
        }
    }

    // False with sealProtected set when the repository refused a Run whose Bundle is still owed.
    public bool TryDeleteRun(
        string runId,
        out IReadOnlyList<string> battleIds,
        out bool sealProtected,
        out Exception? error
    )
    {
        battleIds = Array.Empty<string>();
        sealProtected = false;
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
            var deleted = _repository.DeleteRun(runId);
            battleIds = deleted.BattleIds;
            sealProtected = deleted.SealProtected;
            return !sealProtected;
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

public readonly struct HistoryPanelAttemptResult
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
