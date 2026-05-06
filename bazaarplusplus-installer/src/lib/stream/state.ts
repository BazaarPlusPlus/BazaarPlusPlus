import type { StreamServiceStatus } from '$lib/types';

export interface StreamPageState {
  canCopyUrl: boolean;
  canOpenPreview: boolean;
}

export function createStreamPageState(
  status: StreamServiceStatus
): StreamPageState {
  return {
    canCopyUrl: Boolean(status.running && status.overlay_url),
    canOpenPreview: Boolean(status.running && status.overlay_url)
  };
}
