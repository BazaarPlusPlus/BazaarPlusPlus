import { commandClient } from '../../api/commandClient';
import type { CommandAdapter } from '../../api/commandAdapter';
import type { InstallCommandPort } from './installWorkflow';

type InstallCommandAdapter = Pick<
  CommandAdapter,
  | 'getInstallState'
  | 'chooseGameDirectory'
  | 'installMod'
  | 'resetBppData'
  | 'resetBepinex'
  | 'uninstallMod'
  | 'launchGame'
>;

/** Semantic adapter shared by native and Browser Preview command clients. */
export function createInstallCommandPort(
  commands: InstallCommandAdapter
): InstallCommandPort {
  return {
    loadInstallState: (gamePath) => commands.getInstallState(gamePath ?? null),
    chooseGameDirectory: () => commands.chooseGameDirectory(),
    installMod: (gamePath) => commands.installMod(gamePath),
    resetBppData: (gamePath) => commands.resetBppData(gamePath),
    resetBepinex: (gamePath) => commands.resetBepinex(gamePath),
    uninstallMod: (gamePath) => commands.uninstallMod(gamePath),
    launchGame: async () => {
      await commands.launchGame();
    }
  };
}

export const installCommandPort = createInstallCommandPort(commandClient);
