#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.MonsterPreview;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelDataService
{
    private readonly HistoryPanelRepository? _repository;
    private readonly GhostBattleSyncService? _ghostSyncService;

    public HistoryPanelDataService(
        HistoryPanelRepository? repository,
        GhostBattleSyncService? ghostSyncService = null
    )
    {
        _repository = repository;
        _ghostSyncService = ghostSyncService;
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

    public async Task<HistoryPanelAttemptResult> SyncGhostBattlesAsync(
        CancellationToken cancellationToken
    )
    {
        if (_ghostSyncService == null)
            return HistoryPanelAttemptResult.Failure(
                HistoryPanelText.GhostSyncUnavailable()
            );

        try
        {
            var result = await _ghostSyncService.SyncRecentBattlesAsync(cancellationToken);
            if (!result.Succeeded)
                return HistoryPanelAttemptResult.Failure(
                    HistoryPanelText.GhostSyncFailed(result.Error ?? HistoryPanelText.Unknown())
                );

            return HistoryPanelAttemptResult.Success(
                HistoryPanelText.GhostSyncSucceeded(result.ImportedCount)
            );
        }
        catch (Exception ex)
        {
            return HistoryPanelAttemptResult.Failure(
                HistoryPanelText.GhostSyncFailed(ex.Message),
                ex
            );
        }
    }

    public async Task<HistoryPanelAttemptResult> RefreshFinalBuildsAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            var result = await Task.Run(
                    () =>
                    {
                        var succeeded = CardSetBuildDataRepository.TryRefreshFinalBuildsFromRemote(
                            out var error
                        );
                        return (Succeeded: succeeded, Error: error);
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (!result.Succeeded)
                return HistoryPanelAttemptResult.Failure(
                    HistoryPanelText.FinalBuildRefreshFailed(
                        result.Error ?? HistoryPanelText.Unknown()
                    )
                );

            return HistoryPanelAttemptResult.Success(
                HistoryPanelText.FinalBuildRefreshSucceeded()
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HistoryPanelAttemptResult.Failure(
                HistoryPanelText.FinalBuildRefreshFailed(ex.Message),
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
        Exception? error
    )
    {
        Succeeded = succeeded;
        StatusMessage = statusMessage;
        Error = error;
    }

    public bool Succeeded { get; }

    public string StatusMessage { get; }

    public Exception? Error { get; }

    public static HistoryPanelAttemptResult Success(string statusMessage) =>
        new(true, statusMessage, null);

    public static HistoryPanelAttemptResult Failure(
        string statusMessage,
        Exception? error = null
    ) => new(false, statusMessage, error);
}

