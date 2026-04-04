export type InstallConfirmationStep =
  | 'proceed'
  | 'confirm_game_quit'
  | 'confirm_steam_quit';

export type InstallContinuationAction =
  | 'install'
  | 'show_game_quit_modal'
  | 'show_steam_quit_modal';

export interface InstallConfirmationInput {
  hasTauriRuntime: boolean;
  gameRunning: boolean;
  steamLaunchOptionsSupported: boolean;
  steamRunning: boolean;
}

export function resolveInstallConfirmationStep(
  input: InstallConfirmationInput
): InstallConfirmationStep {
  if (!input.hasTauriRuntime) {
    return 'proceed';
  }

  if (input.gameRunning) {
    return 'confirm_game_quit';
  }

  if (input.steamLaunchOptionsSupported && input.steamRunning) {
    return 'confirm_steam_quit';
  }

  return 'proceed';
}

export function resolveInstallContinuationAction(
  step: InstallConfirmationStep
): InstallContinuationAction {
  if (step === 'confirm_game_quit') {
    return 'show_game_quit_modal';
  }

  if (step === 'confirm_steam_quit') {
    return 'show_steam_quit_modal';
  }

  return 'install';
}
