#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using TheBazaar;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelDataService
{
    private readonly HistoryPanelRepository? _repository;
    private readonly GhostBattleSyncService? _ghostSyncService;
    private readonly Func<string?> _currentPlayerAccountIdAccessor;

    public HistoryPanelDataService(
        HistoryPanelRepository? repository,
        GhostBattleSyncService? ghostSyncService = null,
        Func<string?>? currentPlayerAccountIdAccessor = null
    )
    {
        _repository = repository;
        _ghostSyncService = ghostSyncService;
        _currentPlayerAccountIdAccessor =
            currentPlayerAccountIdAccessor ?? TryGetCurrentPlayerAccountId;
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
            statusMessage = "Run log database path is unavailable.";
            return false;
        }

        try
        {
            runs = _repository.ListRecentRuns(limit);
            statusMessage = _repository.DatabaseExists
                ? $"Loaded {runs.Count} runs from sqlite."
                : "Database file does not exist yet.";
            return true;
        }
        catch (Exception ex)
        {
            statusMessage = $"History load failed: {ex.Message}";
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
            error = new InvalidOperationException("Run log repository is unavailable.");
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
            statusMessage = "Run log database path is unavailable.";
            return false;
        }

        try
        {
            var localPlayerAccountId = _currentPlayerAccountIdAccessor();
            if (string.IsNullOrWhiteSpace(localPlayerAccountId))
            {
                statusMessage = "Current player account is unavailable.";
                return false;
            }

            battles = _repository.ListRecentGhostBattles(localPlayerAccountId, limit);
            statusMessage = $"Loaded {battles.Count} ghost battles from sqlite.";
            return true;
        }
        catch (Exception ex)
        {
            statusMessage = $"Ghost history load failed: {ex.Message}";
            error = ex;
            return false;
        }
    }

    public async Task<HistoryPanelGhostSyncAttemptResult> SyncGhostBattlesAsync(
        CancellationToken cancellationToken
    )
    {
        if (_ghostSyncService == null)
            return HistoryPanelGhostSyncAttemptResult.Failure("Ghost sync is unavailable.");

        try
        {
            var result = await _ghostSyncService.SyncRecentBattlesAsync(cancellationToken);
            if (!result.Succeeded)
                return HistoryPanelGhostSyncAttemptResult.Failure(
                    $"Ghost sync failed: {result.Error ?? "unknown_error"}"
                );

            return HistoryPanelGhostSyncAttemptResult.Success(
                $"Synced {result.ImportedCount} ghost battles."
            );
        }
        catch (Exception ex)
        {
            return HistoryPanelGhostSyncAttemptResult.Failure(
                $"Ghost sync failed: {ex.Message}",
                ex
            );
        }
    }

    private static string? TryGetCurrentPlayerAccountId()
    {
        try
        {
            return BppClientCacheBridge.TryGetProfileAccountId();
        }
        catch
        {
            return null;
        }
    }
}

internal readonly struct HistoryPanelGhostSyncAttemptResult
{
    private HistoryPanelGhostSyncAttemptResult(
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

    public static HistoryPanelGhostSyncAttemptResult Success(string statusMessage) =>
        new(true, statusMessage, null);

    public static HistoryPanelGhostSyncAttemptResult Failure(
        string statusMessage,
        Exception? error = null
    ) => new(false, statusMessage, error);
}
