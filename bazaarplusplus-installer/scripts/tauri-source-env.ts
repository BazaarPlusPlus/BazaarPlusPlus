// `tauri-build` copies resources during source checks, which lack a prepared release ZIP.
// Keep the override child-only so packaging still checks the real resources.
interface TauriConfigOverride {
  bundle?: Record<string, unknown>;
  [key: string]: unknown;
}

export function tauriSourceEnvironment(
  base: NodeJS.ProcessEnv = process.env
): NodeJS.ProcessEnv {
  let config: TauriConfigOverride | null;
  try {
    config = JSON.parse(
      base.TAURI_CONFIG || '{}'
    ) as TauriConfigOverride | null;
  } catch {
    throw new Error('Invalid TAURI_CONFIG for source verification');
  }
  return {
    ...base,
    TAURI_CONFIG: JSON.stringify({
      ...config,
      bundle: { ...config?.bundle, resources: [] }
    })
  };
}
