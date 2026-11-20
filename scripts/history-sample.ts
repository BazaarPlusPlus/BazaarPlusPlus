// Read the analyzer projection so the mod never needs access to another project's config file.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const root = path.resolve(import.meta.dirname, '..');
const projection = path.join(root, 'bazaarplusplus-analyzer', '.env');

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
