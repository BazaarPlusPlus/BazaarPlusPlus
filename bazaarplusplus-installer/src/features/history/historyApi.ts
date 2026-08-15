import { commandClient } from '../../api/commandClient';
import type {
  StorageCleanupPreset,
  StorageCleanupScope
} from '../../types/backend';

export async function listHistoryRuns(limit = 50) {
  return commandClient.listHistoryRuns(limit);
}

/** Resolves to whether a leftover game process was actually terminated. */
export async function endGameProcess() {
  return commandClient.endGameProcess();
}

export async function loadHistoryRunDetail(runId: string) {
  return commandClient.getHistoryRunDetail(runId);
}

export async function revealRunScreenshot(runId: string) {
  await commandClient.revealRunScreenshot(runId);
}

export async function revealBattleVideo(battleId: string, videoId?: string) {
  await commandClient.revealBattleVideo(battleId, videoId ?? null);
}

export async function deleteBattleVideo(battleId: string, videoId: string) {
  return commandClient.deleteBattleVideo(battleId, videoId);
}

export async function previewStorageCleanup(
  scope: StorageCleanupScope,
  preset: StorageCleanupPreset
) {
  return commandClient.previewStorageCleanup(scope, preset);
}

export async function executeStorageCleanup(
  scope: StorageCleanupScope,
  preset: StorageCleanupPreset
) {
  return commandClient.executeStorageCleanup(scope, preset);
}
