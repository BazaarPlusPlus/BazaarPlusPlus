// tauri-build copies bundle resources even during clippy/test/doc. Source
// checks and binding export need no prepared release ZIP. Return a child-only
// override so subsequent packaging still checks the real bundle inputs.
// The TAURI_CONFIG override JSON; only its bundle resources are replaced.
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
