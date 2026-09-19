import fs from 'node:fs';
import path from 'node:path';

export const RELEASE_BASE_URL = 'https://bppinstaller.bazaarplusplus.com';

export const WORKSPACE_ROOT = path.resolve(import.meta.dirname, '..');

export function assertProductVersion(version) {
  if (
    typeof version !== 'string' ||
    !/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(version)
  ) {
    throw new Error(
      `Invalid product version: ${JSON.stringify(version)}; expected major.minor.patch`
    );
  }
  if (
    version
      .split('.')
      .some(
        (part) => !Number.isSafeInteger(Number(part)) || Number(part) > 65534
      )
  ) {
    throw new Error(
      'Product version must fit .NET assembly version fields (0-65534)'
    );
  }
  return version;
}

export function readProductVersion(workspaceRoot = WORKSPACE_ROOT) {
  return assertProductVersion(
    fs.readFileSync(path.join(workspaceRoot, 'VERSION'), 'utf8').trim()
  );
}

export function compareProductVersions(left, right) {
  const a = assertProductVersion(left).split('.').map(Number);
  const b = assertProductVersion(right).split('.').map(Number);
  return a[0] - b[0] || a[1] - b[1] || a[2] - b[2];
}
