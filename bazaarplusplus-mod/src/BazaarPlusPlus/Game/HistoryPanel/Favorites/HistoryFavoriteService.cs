#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.RunLog;

namespace BazaarPlusPlus.Game.HistoryPanel.Favorites;

internal enum FavoriteAddOutcome
{
    Added,
    AddedWithoutReplay,
    AlreadyFavorite,
    Busy,
    Failed,
}

internal readonly record struct FavoriteAddResult(FavoriteAddOutcome Outcome, string? Error = null)
{
    public bool Succeeded =>
        Outcome
            is FavoriteAddOutcome.Added
                or FavoriteAddOutcome.AddedWithoutReplay
                or FavoriteAddOutcome.AlreadyFavorite;
}

// Only the game-dependent operations cross this seam. SQLite, replay copies and their
// lifecycle remain owned by HistoryFavoriteService.
internal delegate SavedBattleLoadOutcome LoadFavoriteLocalReplay(
    string battleId,
    out PvpBattleManifest? manifest,
    out PvpReplayPayload? payload
);

internal delegate bool CanReplayFavorite(out string reason);

// Owns History's Favorites: the favorite_battles rows and each favorite's replay copy under
// FavoriteBattles/. A copy is written and verified before its row is inserted, and one gate
// serializes saves with the orphan sweep so the sweep never deletes a copy whose row is pending.
internal sealed class HistoryFavoriteService
{
    // A copy younger than this may belong to a save in another process; the sweep leaves it.
    private static readonly TimeSpan OrphanGrace = TimeSpan.FromHours(1);

    private readonly HistoryPanelRepository _repository;
    private readonly LoadFavoriteLocalReplay _loadLocalReplay;
    private readonly Func<
        HistoryBattleRecord,
        string,
        CancellationToken,
        Task<GhostReplayLoad>
    > _loadGhostReplay;
    private readonly CanReplayFavorite _canReplay;
    private readonly Func<
        PvpBattleManifest,
        PvpReplayPayload,
        bool,
        CombatReplayPlaybackSource,
        bool
    > _replay;
    private readonly FavoriteBattlePayloadStore? _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HistoryFavoriteService(
        HistoryPanelRepository repository,
        string replayDirectoryPath,
        LoadFavoriteLocalReplay loadLocalReplay,
        Func<HistoryBattleRecord, string, CancellationToken, Task<GhostReplayLoad>> loadGhostReplay,
        CanReplayFavorite canReplay,
        Func<PvpBattleManifest, PvpReplayPayload, bool, CombatReplayPlaybackSource, bool> replay
    )
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _loadLocalReplay =
            loadLocalReplay ?? throw new ArgumentNullException(nameof(loadLocalReplay));
        _loadGhostReplay =
            loadGhostReplay ?? throw new ArgumentNullException(nameof(loadGhostReplay));
        _canReplay = canReplay ?? throw new ArgumentNullException(nameof(canReplay));
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
        if (!string.IsNullOrWhiteSpace(replayDirectoryPath))
            _store = new FavoriteBattlePayloadStore(
                FavoriteBattlePayloadStore.ResolveDirectory(replayDirectoryPath)
            );
    }

    public HistoryCountedPage<HistoryFavoriteRecord> LoadPage(HistoryPageRequest request) =>
        _repository.ListFavorites(request);

    public bool IsFavorite(string battleId) => _repository.IsFavorite(battleId);

    public PvpBattleSnapshots? LoadSnapshots(string battleId) =>
        _repository.LoadFavoriteSnapshots(battleId);

    public bool UpdateNote(string battleId, string note) =>
        _repository.UpdateFavoriteNote(battleId, NormalizeNote(note), DateTimeOffset.UtcNow);

    // Trims surrounding blank lines and caps the note at the schema limit.
    public static string NormalizeNote(string? note)
    {
        var text = (note ?? string.Empty).Replace("\r\n", "\n").Trim();
        return text.Length <= RunLogSchema.FavoriteNoteMaxLength
            ? text
            : text[..RunLogSchema.FavoriteNoteMaxLength];
    }

    // The row goes first: a copy left behind by a failed delete is an orphan the sweep removes.
    public async Task<bool> RemoveAsync(string battleId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var removed = _repository.DeleteFavorite(battleId);
            TryDelete(battleId);
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Local battles load on the caller's (Unity) thread like a saved replay does; the copy is
    // written off it. Ghost battles download first, checked against the account owning the row.
    public async Task<FavoriteAddResult> AddAsync(
        HistoryBattleRecord battle,
        string account,
        CancellationToken cancellationToken
    )
    {
        if (_repository.IsFavorite(battle.BattleId))
            return new(FavoriteAddOutcome.AlreadyFavorite);

        FavoriteBattlePayload? copy = null;
        PvpBattleSnapshots? snapshots = null;
        if (battle.Source == HistoryBattleSource.Ghost)
        {
            var loaded = await _loadGhostReplay(battle, account, cancellationToken);
            if (loaded.Payload != null)
            {
                snapshots = loaded.Payload.BattleManifest.Snapshots;
                copy = new FavoriteBattlePayload
                {
                    BattleId = battle.BattleId,
                    BattleManifest = loaded.Payload.BattleManifest,
                    ReplayPayload = loaded.Payload.ReplayPayload,
                };
            }
        }
        else if (battle.Replay == ReplayAvailability.Saved)
        {
            var outcome = _loadLocalReplay(battle.BattleId, out var manifest, out var payload);
            if (outcome == SavedBattleLoadOutcome.Busy)
                return new(FavoriteAddOutcome.Busy);
            if (outcome == SavedBattleLoadOutcome.Loaded)
                copy = new FavoriteBattlePayload
                {
                    BattleId = battle.BattleId,
                    BattleManifest = manifest!,
                    ReplayPayload = payload!,
                };
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => Save(battle.BattleId, copy, snapshots), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private FavoriteAddResult Save(
        string battleId,
        FavoriteBattlePayload? copy,
        PvpBattleSnapshots? snapshots
    )
    {
        var copySaved = false;
        try
        {
            // Loading happens outside the gate. Another save may have committed meanwhile;
            // recheck under the gate before replacing or rolling back any replay copy.
            if (_repository.IsFavorite(battleId))
                return new(FavoriteAddOutcome.AlreadyFavorite);
            copySaved = copy != null && _store != null && TryWriteVerifiedCopy(copy);
            if (_repository.AddFavorite(battleId, copySaved, snapshots, DateTimeOffset.UtcNow))
                return new(
                    copySaved ? FavoriteAddOutcome.Added : FavoriteAddOutcome.AddedWithoutReplay
                );
            if (copySaved)
                _store!.Delete(battleId);
            return _repository.IsFavorite(battleId)
                ? new(FavoriteAddOutcome.AlreadyFavorite)
                : new(FavoriteAddOutcome.Failed, HistoryPanelText.FavoriteBattleNotFound());
        }
        catch (Exception ex)
        {
            if (copySaved)
                TryDelete(battleId);
            return new(FavoriteAddOutcome.Failed, ex.Message);
        }
    }

    private bool TryWriteVerifiedCopy(FavoriteBattlePayload copy)
    {
        try
        {
            _store!.Save(copy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(copy.BattleId);
            return false;
        }
        var reloaded = _store.LoadDetailed(copy.BattleId);
        if (
            reloaded.Status == FileBackedPayloadLoadStatus.Loaded
            && IsReplayable(reloaded.Payload, copy.BattleId)
        )
            return true;
        TryDelete(copy.BattleId);
        return false;
    }

    public bool CanReplay(HistoryFavoriteRecord? favorite, out string reason)
    {
        if (favorite == null)
        {
            reason = HistoryPanelText.SelectBattleToReplay();
            return false;
        }
        if (favorite.Battle.Replay != ReplayAvailability.Saved || _store == null)
        {
            reason = HistoryPanelText.FavoriteReplayUnavailable();
            return false;
        }
        return _canReplay(out reason);
    }

    public async Task<HistoryPanelReplayAttemptResult> ReplayAsync(
        HistoryFavoriteRecord? favorite,
        bool recordVideo,
        CancellationToken cancellationToken,
        Func<bool>? canCommit = null
    )
    {
        if (!CanReplay(favorite, out var reason))
            return HistoryPanelReplayAttemptResult.Failure(
                reason,
                HistoryPanelReplayReasonCode.ReplayUnavailable
            );
        var battleId = favorite!.BattleId;
        var loaded = await Task.Run(() => _store!.LoadDetailed(battleId), cancellationToken);
        if (
            loaded.Status != FileBackedPayloadLoadStatus.Loaded
            || !IsReplayable(loaded.Payload, battleId)
        )
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.ReplayPayloadUnavailable(battleId),
                loaded.Status == FileBackedPayloadLoadStatus.Missing
                    ? HistoryPanelReplayReasonCode.ReplayPayloadMissing
                    : HistoryPanelReplayReasonCode.ReplayPayloadInvalid,
                loaded.Exception
            );

        cancellationToken.ThrowIfCancellationRequested();
        if (canCommit?.Invoke() == false)
            throw new OperationCanceledException(
                "History selection is no longer active.",
                cancellationToken
            );
        if (
            !_replay(
                loaded.Payload!.BattleManifest,
                loaded.Payload.ReplayPayload,
                recordVideo,
                CombatReplayPlaybackSource.Favorite
            )
        )
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.ReplayRejectedForBattle(battleId),
                HistoryPanelReplayReasonCode.ReplayRejected
            );
        return HistoryPanelReplayAttemptResult.Success(
            HistoryPanelText.StartingReplayForBattle(battleId)
        );
    }

    // Removes copies whose favorite is gone: a crash between writing a copy and inserting its row,
    // or a delete that could not remove the file. Returns how many copies it removed.
    public async Task<int> SweepOrphanCopiesAsync(CancellationToken cancellationToken)
    {
        if (_store == null || !_repository.DatabaseExists)
            return 0;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(
                () =>
                {
                    var kept = _repository.ListFavoriteBattleIds();
                    var cutoff = DateTime.UtcNow - OrphanGrace;
                    var removed = 0;
                    foreach (var id in _store.ListIds().ToList())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (kept.Contains(id) || _store.LastWriteTimeUtc(id) > cutoff)
                            continue;
                        if (TryDelete(id))
                            removed++;
                    }
                    return removed;
                },
                cancellationToken
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsReplayable(FavoriteBattlePayload? payload, string battleId) =>
        payload != null
        && payload.BattleId == battleId
        && payload.BattleManifest?.BattleId == battleId
        && payload.ReplayPayload
            is { SpawnMessageBytes.Length: > 0, CombatMessageBytes.Length: > 0 };

    private bool TryDelete(string battleId)
    {
        try
        {
            _store?.Delete(battleId);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
