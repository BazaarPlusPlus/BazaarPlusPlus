import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

import type { TauriCommandMap } from './tauri';
import {
  TAURI_COMMAND_NAMES,
  type TauriCommandName
} from '../types/generated/tauri-command-names';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(scriptDir, '../..');
type InputFieldChecklist = {
  [K in TauriCommandName]: readonly (keyof NonNullable<
    TauriCommandMap[K]['input']
  > &
    string)[];
};

const COMMAND_INPUT_FIELDS = {
  get_app_bootstrap: [],
  set_app_locale: ['locale'],
  get_install_state: ['gamePath'],
  choose_game_directory: [],
  install_mod: ['gamePath'],
  reset_bpp_data: ['gamePath'],
  uninstall_mod: ['gamePath'],
  launch_game: ['gamePath'],
  cancel_tempo_launch: [],
  get_stream_status: [],
  ensure_stream_session: ['gamePath'],
  restart_stream_session: ['gamePath'],
  set_stream_window: ['gamePath', 'offset'],
  get_overlay_settings: [],
  save_overlay_display_mode: ['displayMode'],
  apply_overlay_crop_code: ['code'],
  reset_overlay_crop: [],
  list_history_runs: ['gamePath', 'limit'],
  get_history_run_detail: ['gamePath', 'runId'],
  reveal_run_screenshot: ['gamePath', 'runId'],
  reveal_battle_video: ['gamePath', 'battleId', 'videoId'],
  delete_battle_video: ['gamePath', 'battleId', 'videoId'],
  delete_run_videos: ['gamePath', 'runId', 'limit']
} satisfies InputFieldChecklist;

function parseWithCommandsNames(): string[] {
  const source = readFileSync(
    path.join(projectRoot, 'src-tauri/src/commands/registry.rs'),
    'utf8'
  );
  const productionSource = source.split('#[cfg(test)]')[0];
  const listMarker = '$macro! {';
  const listStart = productionSource.indexOf(listMarker);
  if (listStart === -1) {
    throw new Error(
      'Could not find with_commands command list in commands/registry.rs'
    );
  }
  const bodyContentStart = listStart + listMarker.length;
  const listEnd = productionSource.indexOf('\n        }', bodyContentStart);
  if (listEnd === -1) {
    throw new Error('Could not find with_commands command list end');
  }
  const block = productionSource.slice(bodyContentStart, listEnd);
  return block
    .split('\n')
    .map((line) => line.trim().replace(/[),]+$/, ''))
    .map((line) => line.split(',').pop()?.trim() ?? '')
    .filter((name) => /^[a-z][a-z0-9_]*$/.test(name));
}

describe('Tauri command registry', () => {
  it('generated names match with_commands list in registry.rs', () => {
    expect([...TAURI_COMMAND_NAMES]).toEqual(parseWithCommandsNames());
  });

  it('every registered command has a TauriCommandMap entry', () => {
    for (const name of TAURI_COMMAND_NAMES) {
      type AssertMapped = typeof name extends keyof TauriCommandMap
        ? true
        : false;
      const mapped: AssertMapped = true;
      expect(mapped).toBe(true);
    }
  });

  it('tracks command input fields against the frontend command map', () => {
    expect(Object.keys(COMMAND_INPUT_FIELDS)).toEqual([...TAURI_COMMAND_NAMES]);
  });
});
