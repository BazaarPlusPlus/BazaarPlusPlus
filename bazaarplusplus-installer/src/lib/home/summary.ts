import type { EnvironmentInfo, StreamServiceStatus } from '$lib/types';

export type HomeCardTone = 'idle' | 'active';

export interface HomeSummaryCard {
  tone: HomeCardTone;
  title: string;
  detail: string;
}

export interface HomeSummary {
  install: HomeSummaryCard;
  stream: HomeSummaryCard;
}

export function createHomeSummary(input: {
  env: EnvironmentInfo | null;
  streamStatus: StreamServiceStatus;
}): HomeSummary {
  const installed = Boolean(input.env?.bepinex_installed);
  const version =
    input.env?.bpp_version ?? input.env?.bundled_bpp_version ?? 'unknown';

  return {
    install: {
      tone: installed ? 'active' : 'idle',
      title: installed
        ? 'BazaarPlusPlus installed'
        : 'BazaarPlusPlus not installed',
      detail: installed
        ? `Version ${version}`
        : 'Open Install & Repair to continue.'
    },
    stream: {
      tone: input.streamStatus.running ? 'active' : 'idle',
      title: input.streamStatus.running
        ? 'Stream mode running'
        : 'Stream mode stopped',
      detail: input.streamStatus.running
        ? `OBS URL: ${input.streamStatus.overlay_url}`
        : 'Open Stream Mode when you are ready to stream.'
    }
  };
}
