import { commandClient } from '../../api/commandClient';
import { hasTauriRuntime } from '../../api/runtime';
import type { AppBootstrapLoadResult } from './appBootstrap';

export async function loadAppBootstrap(): Promise<AppBootstrapLoadResult> {
  const data = await commandClient.getAppBootstrap();
  return {
    source: hasTauriRuntime() ? 'native' : 'preview',
    data
  };
}
