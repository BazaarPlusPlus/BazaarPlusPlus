import type { DotnetInfo, EnvironmentInfo } from '$lib/types';
import type { StepState } from '$lib/installer/state';

export interface DetectInstallerEnvironmentOptions {
  requestedGamePath: string | null;
  detectEnvironment: (gamePath?: string) => Promise<EnvironmentInfo>;
  detectDotnetRuntime: () => Promise<DotnetInfo>;
  verifyGamePath: (path: string) => Promise<boolean>;
}

export interface DetectInstallerEnvironmentResult {
  env: EnvironmentInfo | null;
  dotnetState: StepState;
  bazaarFound: boolean;
  bazaarInvalid: boolean;
}

function resolveDotnetState(result: DotnetInfo | null): StepState {
  if (!result) {
    return 'idle';
  }

  return result.dotnet_ok ? 'found' : 'not_found';
}

function mergeEnvironmentWithDotnet(
  env: EnvironmentInfo,
  dotnetInfo: DotnetInfo | null
): EnvironmentInfo {
  if (!dotnetInfo) {
    return env;
  }

  return {
    ...env,
    ...dotnetInfo
  };
}

async function resolveBazaarState(
  requestedGamePath: string | null,
  detectedGamePath: string | null,
  verifyGamePath: (path: string) => Promise<boolean>
): Promise<
  Pick<DetectInstallerEnvironmentResult, 'bazaarFound' | 'bazaarInvalid'>
> {
  const pathToVerify = requestedGamePath ?? detectedGamePath;
  if (!pathToVerify) {
    return {
      bazaarFound: false,
      bazaarInvalid: false
    };
  }

  const bazaarFound = await verifyGamePath(pathToVerify);
  return {
    bazaarFound,
    bazaarInvalid: !bazaarFound
  };
}

export async function detectInstallerEnvironment(
  options: DetectInstallerEnvironmentOptions
): Promise<DetectInstallerEnvironmentResult> {
  const dotnetPromise = options.detectDotnetRuntime().catch(() => null);

  try {
    const env = await options.detectEnvironment(
      options.requestedGamePath ?? undefined
    );
    const [dotnetInfo, bazaarState] = await Promise.all([
      dotnetPromise,
      resolveBazaarState(
        options.requestedGamePath,
        env.game_path,
        options.verifyGamePath
      )
    ]);

    return {
      env: mergeEnvironmentWithDotnet(env, dotnetInfo),
      dotnetState: resolveDotnetState(dotnetInfo),
      bazaarFound: bazaarState.bazaarFound,
      bazaarInvalid: bazaarState.bazaarInvalid
    };
  } catch {
    await dotnetPromise;

    return {
      env: null,
      dotnetState: 'idle',
      bazaarFound: false,
      bazaarInvalid: false
    };
  }
}
