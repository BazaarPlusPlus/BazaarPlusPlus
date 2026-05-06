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
            reason = HistoryPanelText.SelectBattleToReplay();
            return false;
        }

        var runtime = _runtimeAccessor();
        if (runtime == null)
        {
            reason = HistoryPanelText.CombatReplayRuntimeUnavailable();
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

            reason = HistoryPanelText.GhostReplayPayloadUnavailable();
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
            ? HistoryPanelText.DownloadReplay()
            : HistoryPanelText.Replay();
    }

    public async Task<HistoryPanelReplayAttemptResult> ReplayBattleAsync(
        HistoryBattleRecord? battle,
        CancellationToken cancellationToken
    )
    {
        if (battle == null)
            return HistoryPanelReplayAttemptResult.Failure(HistoryPanelText.SelectBattleToReplay());

        if (!CanReplayBattle(battle, out var reason))
            return HistoryPanelReplayAttemptResult.Failure(reason);

        if (battle.Source == HistoryBattleSource.Ghost)
            return await ReplayGhostBattleAsync(battle, cancellationToken);

        var runtime = _runtimeAccessor();
        if (runtime == null)
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.CombatReplayRuntimeUnavailable()
            );

        if (!runtime.ReplaySaved(battle.BattleId))
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.ReplayRejectedForBattle(battle.BattleId)
            );

        return HistoryPanelReplayAttemptResult.Success(
            HistoryPanelText.StartingReplayForBattle(battle.BattleId)
        );
    }

    private async Task<HistoryPanelReplayAttemptResult> ReplayGhostBattleAsync(
        HistoryBattleRecord battle,
        CancellationToken cancellationToken
    )
    {
        var runtime = _runtimeAccessor();
        if (runtime == null)
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.CombatReplayRuntimeUnavailable()
            );

        var replayDirectoryPath = _replayDirectoryPathAccessor();
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.CombatReplayDirectoryUnavailable()
            );

        if (!battle.ReplayDownloaded)
        {
            if (_ghostSyncService == null)
                return HistoryPanelReplayAttemptResult.Failure(
                    HistoryPanelText.GhostReplayDownloadUnavailable()
                );

            var downloadResult = await _ghostSyncService.DownloadReplayAsync(
                battle.BattleId,
                replayDirectoryPath,
                cancellationToken
            );
            if (!downloadResult.Succeeded)
                return HistoryPanelReplayAttemptResult.Failure(
                    HistoryPanelText.FailedToDownloadGhostReplay(
                        downloadResult.Error ?? HistoryPanelText.Unknown()
                    )
                );
        }

        var ghostPayloadStore = new GhostBattlePayloadStore(
            BuildGhostBattlePayloadDirectoryPath(replayDirectoryPath)
        );
        var ghostPayload = ghostPayloadStore.Load(battle.BattleId);
        var manifest = ghostPayload?.BattleManifest;
        if (manifest == null)
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.GhostManifestUnavailable(battle.BattleId)
            );

        var payload = ghostPayload?.ReplayPayload;
        if (payload == null)
        {
            var payloadStore = new CombatReplayPayloadStore(replayDirectoryPath);
            payload = payloadStore.Load(battle.BattleId);
        }
        if (payload == null)
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.ReplayPayloadUnavailable(battle.BattleId)
            );

        if (!runtime.ReplayImportedBattle(manifest, payload))
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.ReplayRejectedForGhostBattle(battle.BattleId)
            );

        return HistoryPanelReplayAttemptResult.Success(
            battle.ReplayDownloaded
                ? HistoryPanelText.StartingReplayForBattle(battle.BattleId)
                : HistoryPanelText.DownloadedAndStartingReplay(battle.BattleId)
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
                    HistoryPanelText.DeletePayloadFailed(battleId, ex.Message)
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
