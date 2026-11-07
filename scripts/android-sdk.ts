import os from 'node:os';
import path from 'node:path';

/** Resolve SDK executables for both standalone commands and device setup. */
export function androidSdkPaths(
  env: NodeJS.ProcessEnv = process.env,
  platform: NodeJS.Platform = process.platform,
  home = os.homedir()
) {
  const paths = platform === 'win32' ? path.win32 : path.posix;
  const sdk =
    env.ANDROID_HOME ||
    (platform === 'win32'
      ? paths.join(
          env.LOCALAPPDATA || paths.join(home, 'AppData/Local'),
          'Android/Sdk'
        )
      : paths.join(
          home,
          platform === 'darwin' ? 'Library/Android/sdk' : 'Android/Sdk'
        ));
  const executable = platform === 'win32' ? '.exe' : '';
  return {
    sdk,
    avds: env.ANDROID_AVD_HOME || paths.join(home, '.android/avd'),
    adb: env.ADB || paths.join(sdk, `platform-tools/adb${executable}`),
    emulator: paths.join(sdk, `emulator/emulator${executable}`),
    qemuImg: paths.join(sdk, `emulator/qemu-img${executable}`)
  };
}
