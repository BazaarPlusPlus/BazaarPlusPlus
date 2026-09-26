// tauri-build copies bundle resources even during clippy/test/doc. Source
// checks and binding export need no prepared release ZIP. Return a child-only
// override so subsequent packaging still checks the real bundle inputs.
export function tauriSourceEnvironment(base = process.env) {
  let config;
  try {
    config = JSON.parse(base.TAURI_CONFIG || '{}');
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
