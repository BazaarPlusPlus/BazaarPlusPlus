import { invokeCommand } from '../../api/tauri';
import { hasTauriRuntime } from '../../api/runtime';
import type { AppBootstrap } from '../../types/backend';
import bootstrapResource from '../../../src-tauri/resources/app-bootstrap.json';

export const fallbackBootstrap: AppBootstrap = {
  ...(bootstrapResource as Pick<
    AppBootstrap,
    'links' | 'credits' | 'licenses'
  >),
  app_version: __FRONTEND_VERSION__,
  bundled_bpp_version: null
};

export async function loadAppBootstrap() {
  if (!hasTauriRuntime()) {
    return fallbackBootstrap;
  }

  return invokeCommand('get_app_bootstrap');
}
