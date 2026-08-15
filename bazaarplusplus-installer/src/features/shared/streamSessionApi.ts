import { commandClient } from '../../api/commandClient';

export async function ensureStreamSession() {
  // This intentionally starts the local HTTP service when it is not running.
  return commandClient.ensureStreamSession(null);
}

export async function getStreamStatus() {
  return commandClient.getStreamStatus();
}
