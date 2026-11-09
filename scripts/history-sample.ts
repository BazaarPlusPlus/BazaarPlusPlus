// Development only: runs `just mod::history-sample` with the bundle_sync token. The token is
// config.toml's [server] BUNDLE_SYNC_TOKEN, which reaches this checkout only inside the analyzer
// projection (`just with-config analyzer` refreshes it), so the root reads it there and the mod
// never reads another project's file. An environment value wins.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const root = path.resolve(import.meta.dirname, '..');
const projection = path.join(root, 'bazaarplusplus-analyzer', '.env');

/** The value of `key` in a dotenv file's text, without surrounding quotes. */
export function dotenvValue(text: string, key: string): string | undefined {
  for (const line of text.split(/\r?\n/)) {
    const match = /^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$/.exec(line);
    if (match?.[1] !== key) continue;
    const value = match[2].trim().replace(/^(['"])(.*)\1$/, '$2');
    return value.length > 0 ? value : undefined;
  }
  return undefined;
}

if (import.meta.main) {
  const env = { ...process.env };
  if (!env.BPP_BUNDLE_SYNC_TOKEN && fs.existsSync(projection)) {
    const token = dotenvValue(
      fs.readFileSync(projection, 'utf8'),
      'BPP_BUNDLE_SYNC_TOKEN'
    );
    if (token) env.BPP_BUNDLE_SYNC_TOKEN = token;
  }
  const result = spawnSync(
    'just',
    ['mod::history-sample', ...process.argv.slice(2)],
    {
      cwd: root,
      env,
      stdio: 'inherit'
    }
  );
  process.exit(result.status ?? 1);
}
