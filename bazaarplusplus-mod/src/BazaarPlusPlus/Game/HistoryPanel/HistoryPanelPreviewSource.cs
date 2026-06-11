#nullable enable
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;

namespace BazaarPlusPlus.Game.HistoryPanel;

// Decides what cards the preview renderer should show, given the current selection.
// The coordinator owns selection state; this class is a pure projection on top of records
// plus an optional disk lookup for ghost payloads that the repository does not load eagerly.
internal sealed class HistoryPanelPreviewSource
{
    private readonly IHistoryPanelRuntime _runtime;

    public HistoryPanelPreviewSource(IHistoryPanelRuntime runtime)
    {
        _runtime = runtime;
    }

    public HistoryBattlePreviewData Build(
        PreviewSelectionMode previewSelectionMode,
        HistorySectionMode sectionMode,
        HistoryBattleRecord? activeSelectedBattle,
        HistoryRunRecord? selectedRun,
        IReadOnlyList<HistoryBattleRecord> runBattles
    )
    {
        if (previewSelectionMode == PreviewSelectionMode.Battle && activeSelectedBattle != null)
        {
            var signature = $"battle:{activeSelectedBattle.BattleId}";
            return sectionMode == HistorySectionMode.Ghost
                ? ResolveGhostPreviewData(activeSelectedBattle, signature)
                : HistoryBattlePreviewProjection.BuildOpponent(
                    activeSelectedBattle.Snapshots,
                    signature
                );
        }

        var runPreviewBattle = PickRunPreviewBattle(runBattles);
        if (runPreviewBattle != null)
        {
            return HistoryBattlePreviewProjection.BuildPlayer(
                runPreviewBattle.Snapshots,
                $"run:{selectedRun?.RunId}:{runPreviewBattle.BattleId}"
            );
        }

        return HistoryBattlePreviewData.Empty;
    }

    // Ghost replay payload snapshots stay in the uploader's original perspective.
    // For the local "against me" view, our board is stored on the opponent side.
    private HistoryBattlePreviewData ResolveGhostPreviewData(
        HistoryBattleRecord battle,
        string signature
    )
    {
        if (battle.Source != HistoryBattleSource.Ghost)
            return HistoryBattlePreviewProjection.BuildOpponent(battle.Snapshots, signature);

        var replayDirectoryPath = _runtime.CombatReplayDirectoryPath;
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return HistoryBattlePreviewProjection.BuildEmpty(signature);

        var ghostPayloadStore = new GhostBattlePayloadStore(
            GhostBattlePayloadStore.ResolveDirectory(replayDirectoryPath)
        );
        var ghostPayload = ghostPayloadStore.Load(battle.BattleId);
        var snapshots = ghostPayload?.BattleManifest?.Snapshots;
        if (snapshots == null)
            return HistoryBattlePreviewProjection.BuildEmpty(signature);

        return HistoryBattlePreviewProjection.BuildOpponent(snapshots, signature);
    }

    private static HistoryBattleRecord? PickRunPreviewBattle(
        IReadOnlyList<HistoryBattleRecord> runBattles
    )
    {
        if (runBattles.Count == 0)
            return null;

        return runBattles
            .OrderByDescending(battle => battle.Day ?? int.MinValue)
            .ThenByDescending(battle => battle.Hour ?? int.MinValue)
            .ThenByDescending(battle => battle.RecordedAtUtc)
            .FirstOrDefault();
    }
}
