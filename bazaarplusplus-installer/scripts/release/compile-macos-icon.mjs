import fs from 'node:fs';
import path from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';

const rootDir = path.resolve(import.meta.dirname, '..', '..');
const appIconName = 'AppIcon';
const iconSource = path.join(
  rootDir,
  'src-tauri',
  'icons',
  'source',
  `${appIconName}.icon`
);
const outputDir = path.join(rootDir, 'src-tauri', 'icons', 'macos-generated');

function isMacOSBuild() {
  const platform = process.env.TAURI_ENV_PLATFORM ?? process.platform;
  return platform === 'macos' || platform === 'darwin';
}

function commandWorks(command, args) {
  const result = spawnSync(command, args, {
    encoding: 'utf8',
    stdio: 'pipe'
  });
  return result.status === 0;
}

function findActool() {
  const candidates = [];
  if (process.env.BPP_ACTOOL) candidates.push(process.env.BPP_ACTOOL);
  if (process.env.DEVELOPER_DIR) {
    candidates.push(
      path.join(process.env.DEVELOPER_DIR, 'usr', 'bin', 'actool'),
      path.join(
        process.env.DEVELOPER_DIR,
        'Contents',
        'Developer',
        'usr',
        'bin',
        'actool'
      )
    );
  }
  candidates.push(
    '/Applications/Xcode.app/Contents/Developer/usr/bin/actool',
    '/usr/bin/actool'
  );

  for (const candidate of candidates) {
    if (fs.existsSync(candidate) && commandWorks(candidate, ['--version'])) {
      return candidate;
    }
  }

  const xcrun = spawnSync('xcrun', ['--find', 'actool'], {
    encoding: 'utf8',
    stdio: 'pipe'
  });
  if (xcrun.status === 0) {
    const candidate = xcrun.stdout.trim();
    if (candidate && commandWorks(candidate, ['--version'])) return candidate;
  }

  return null;
}

export function compileMacOSIcon({ allowFlatFallback = false } = {}) {
  if (!isMacOSBuild()) {
    return { platform: 'skipped', compiled: false };
  }

  if (!fs.existsSync(iconSource)) {
    throw new Error(`Missing macOS Icon Composer source: ${iconSource}`);
  }

  const actool = findActool();
  if (!actool) {
    if (allowFlatFallback) {
      console.warn(
        'compile-macos-icon: Xcode actool is unavailable; keeping the checked-in ICNS fallback'
      );
      return { platform: 'macos', compiled: false, fallback: true };
    }
    throw new Error(
      'Xcode 26 actool is required to compile the macOS .icon asset. ' +
        'Install Xcode or set BPP_ACTOOL to its actool path. ' +
        'For a local flat-icon test only, set BPP_ALLOW_FLAT_MACOS_ICON_FALLBACK=1.'
    );
  }

  fs.rmSync(outputDir, { force: true, recursive: true });
  fs.mkdirSync(outputDir, { recursive: true });
  const partialInfoPlist = path.join(
    outputDir,
    'assetcatalog_generated_info.plist'
  );

  execFileSync(
    actool,
    [
      iconSource,
      '--app-icon',
      appIconName,
      '--compile',
      outputDir,
      '--output-partial-info-plist',
      partialInfoPlist,
      '--minimum-deployment-target',
      '11.0',
      '--platform',
      'macosx',
      '--target-device',
      'mac'
    ],
    { cwd: rootDir, stdio: 'inherit' }
  );

  for (const filename of ['Assets.car', 'AppIcon.icns']) {
    const output = path.join(outputDir, filename);
    if (!fs.existsSync(output)) {
      throw new Error(`actool did not produce ${output}`);
    }
  }

  fs.rmSync(partialInfoPlist, { force: true });
  return { platform: 'macos', compiled: true, outputDir };
}

if (import.meta.main) {
  compileMacOSIcon({
    allowFlatFallback: process.env.BPP_ALLOW_FLAT_MACOS_ICON_FALLBACK === '1'
  });
}
