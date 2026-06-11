import { composeStripPreviewUrl } from '../../api/http';

export function optionalStripPreviewUrl(
  baseUrl: string | null | undefined,
  stripUrl: string | null | undefined
): string | null {
  if (!baseUrl || !stripUrl) {
    return null;
  }

  try {
    return composeStripPreviewUrl(baseUrl, stripUrl);
  } catch {
    return null;
  }
}
