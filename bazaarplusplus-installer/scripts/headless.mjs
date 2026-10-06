// Run the installer CLI from source with no prepared release Payload required.
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { tauriSourceEnvironment } from './tauri-source-env.mjs';

const result = spawnSync(
  'cargo',
  ['run', '--quiet', '--bin', 'bppinstaller', '--', ...process.argv.slice(2)],
  {
    cwd: fileURLToPath(new URL('../src-tauri/', import.meta.url)),
    env: tauriSourceEnvironment(),
    stdio: 'inherit'
  }
);
if (result.error) throw result.error;
process.exit(result.status ?? 1);
