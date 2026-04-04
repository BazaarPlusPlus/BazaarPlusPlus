#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelReplayService
{
    private readonly Func<CombatReplayRuntime?> _runtimeAccessor;
    private readonly Func<string?> _replayDirectoryPathAccessor;
    private readonly GhostBattleSyncService? _ghostSyncService;

    public HistoryPanelReplayService(
        Func<CombatReplayRuntime?> runtimeAccessor,
        Func<string?> replayDirectoryPathAccessor,
        GhostBattleSyncService? ghostSyncService = null
    )
    {
        _runtimeAccessor =
            runtimeAccessor ?? throw new ArgumentNullException(nameof(runtimeAccessor));
        _replayDirectoryPathAccessor =
            replayDirectoryPathAccessor
            ?? throw new ArgumentNullException(nameof(replayDirectoryPathAccessor));
        _ghostSyncService = ghostSyncService;
    }

    public bool CanReplayBattle(HistoryBattleRecord? battle, out string reason)
    {
        if (battle == null)
        {
            reason = "Select a battle to replay.";
            return false;
        }

        var runtime = _runtimeAccessor();
        if (runtime == null)
        {
            reason = "Combat replay runtime is unavailable.";
            return false;
        }

        if (battle.Source == HistoryBattleSource.Ghost)
        {
            if (!runtime.CanReplaySavedCombats(out reason))
                return false;

            if (battle.ReplayDownloaded || battle.ReplayAvailable)
            {
                reason = string.Empty;
                return true;
            }

            reason = "Replay payload for the selected ghost battle is unavailable.";
            return false;
        }

        return runtime.CanReplaySavedBattle(battle.BattleId, out reason);
    }

    public string GetReplayActionLabel(HistoryBattleRecord? battle)
    {
        return
            battle?.Source == HistoryBattleSource.Ghost
            && !battle.ReplayDownloaded
            && battle.ReplayAvailable
            ? "Download Replay"
            : "Replay";
    }

    public async Task<HistoryPanelReplayAttemptResult> ReplayBattleAsync(
        HistoryBattleRecord? battle,
        CancellationToken cancellationToken
    )
    {
        if (battle == null)
            return HistoryPanelReplayAttemptResult.Failure("Select a battle to replay.");

        if (!CanReplayBattle(battle, out var reason))
            return HistoryPanelReplayAttemptResult.Failure(reason);

        if (battle.Source == HistoryBattleSource.Ghost)
            return await ReplayGhostBattleAsync(battle, cancellationToken);

        var runtime = _runtimeAccessor();
        if (runtime == null)
            return HistoryPanelReplayAttemptResult.Failure("Combat replay runtime is unavailable.");

        if (!runtime.ReplaySaved(battle.BattleId))
            return HistoryPanelReplayAttemptResult.Failure(
                $"Replay rejected for battle {battle.BattleId}."
            );

        return HistoryPanelReplayAttemptResult.Success($"Starting replay for {battle.BattleId}.");
    }

    private async Task<HistoryPanelReplayAttemptResult> ReplayGhostBattleAsync(
        HistoryBattleRecord battle,
        CancellationToken cancellationToken
    )
    {
        var runtime = _runtimeAccessor();
        if (runtime == null)
            return HistoryPanelReplayAttemptResult.Failure("Combat replay runtime is unavailable.");

        var replayDirectoryPath = _replayDirectoryPathAccessor();
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return HistoryPanelReplayAttemptResult.Failure(
                "Combat replay directory path is unavailable."
            );

        if (!battle.ReplayDownloaded)
        {
            if (_ghostSyncService == null)
                return HistoryPanelReplayAttemptResult.Failure(
                    "Ghost replay download is unavailable."
                );

            var downloadResult = await _ghostSyncService.DownloadReplayAsync(
                battle.BattleId,
                replayDirectoryPath,
                cancellationToken
            );
            if (!downloadResult.Succeeded)
                return HistoryPanelReplayAttemptResult.Failure(
                    $"Failed to download ghost replay: {downloadResult.Error ?? "unknown_error"}"
                );
        }

        var ghostPayloadStore = new GhostBattlePayloadStore(
            BuildGhostBattlePayloadDirectoryPath(replayDirectoryPath)
        );
        var ghostPayload = ghostPayloadStore.Load(battle.BattleId);
        var manifest = ghostPayload?.BattleManifest;
        if (manifest == null)
            return HistoryPanelReplayAttemptResult.Failure(
                $"Ghost manifest for battle {battle.BattleId} is unavailable."
            );

        var payload = ghostPayload?.ReplayPayload;
        if (payload == null)
        {
            var payloadStore = new CombatReplayPayloadStore(replayDirectoryPath);
            payload = payloadStore.Load(battle.BattleId);
        }
        if (payload == null)
            return HistoryPanelReplayAttemptResult.Failure(
                $"Replay payload for battle {battle.BattleId} is unavailable."
            );

        if (!runtime.ReplayImportedBattle(manifest, payload))
            return HistoryPanelReplayAttemptResult.Failure(
                $"Replay rejected for ghost battle {battle.BattleId}."
            );

        return HistoryPanelReplayAttemptResult.Success(
            battle.ReplayDownloaded
                ? $"Starting replay for {battle.BattleId}."
                : $"Downloaded and starting replay for {battle.BattleId}."
        );
    }

    public void CleanupReplayPayloads(IReadOnlyList<string> battleIds)
    {
        if (battleIds.Count == 0)
            return;

        var replayDirectoryPath = _replayDirectoryPathAccessor();
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return;

        var payloadStore = new CombatReplayPayloadStore(replayDirectoryPath);
        var ghostPayloadStore = new GhostBattlePayloadStore(
            BuildGhostBattlePayloadDirectoryPath(replayDirectoryPath)
        );
        foreach (var battleId in battleIds)
        {
            try
            {
                payloadStore.Delete(battleId);
                ghostPayloadStore.Delete(battleId);
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "HistoryPanel",
                    $"Failed to delete replay payload for battle {battleId}: {ex.Message}"
                );
            }
        }
    }

    private static string BuildGhostBattlePayloadDirectoryPath(string replayDirectoryPath)
    {
        var parentDirectory = System.IO.Path.GetDirectoryName(replayDirectoryPath);
        return string.IsNullOrWhiteSpace(parentDirectory)
            ? System.IO.Path.Combine(replayDirectoryPath, "GhostBattlePayloads")
            : System.IO.Path.Combine(parentDirectory, "GhostBattlePayloads");
    }
}

internal readonly struct HistoryPanelReplayAttemptResult
{
    private HistoryPanelReplayAttemptResult(bool succeeded, string statusMessage)
    {
        Succeeded = succeeded;
        StatusMessage = statusMessage;
    }

    public bool Succeeded { get; }

    public string StatusMessage { get; }

    public static HistoryPanelReplayAttemptResult Success(string statusMessage) =>
        new(true, statusMessage);

    public static HistoryPanelReplayAttemptResult Failure(string statusMessage) =>
        new(false, statusMessage);
}
