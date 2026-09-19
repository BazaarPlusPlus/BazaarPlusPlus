import { commandClient } from '../../api/commandClient';

export async function getStreamStatus() {
  return commandClient.getStreamStatus();
}
