export type InstallRuntimeRisk = 'steam_running';

export interface InstallConfirmationInput {
  hasTauriRuntime: boolean;
  steamLaunchOptionsSupported: boolean;
  steamRunning: boolean;
}

export function getInstallRuntimeRisks(
  input: InstallConfirmationInput
): InstallRuntimeRisk[] {
  if (!input.hasTauriRuntime) {
    return [];
  }

  const risks: InstallRuntimeRisk[] = [];

  if (input.steamLaunchOptionsSupported && input.steamRunning) {
    risks.push('steam_running');
  }

  return risks;
}

export function shouldShowInstallRiskModal(
  risks: InstallRuntimeRisk[]
): boolean {
  return risks.length > 0;
}
