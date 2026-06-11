const ABSOLUTE_SCHEME_PATTERN = /^[a-zA-Z][a-zA-Z\d+.-]*:/;

export function joinServiceUrl(baseUrl: string, relativePath: string): string {
  const base = normalizeBaseUrl(baseUrl);
  const path = normalizeRelativePath(relativePath, 'relative URL');
  return new URL(path.replace(/^\/+/, ''), `${base}/`).toString();
}

export function composeStripPreviewUrl(
  baseUrl: string,
  stripUrl: string
): string {
  return joinServiceUrl(
    baseUrl,
    normalizeRelativePath(stripUrl, 'relative strip URL')
  );
}

function normalizeBaseUrl(baseUrl: string): string {
  const trimmed = baseUrl.trim();
  if (!trimmed) {
    throw new Error('Stream service base URL is required.');
  }

  const parsed = new URL(trimmed);
  parsed.pathname = parsed.pathname.replace(/\/+$/, '');
  parsed.search = '';
  parsed.hash = '';
  return parsed.toString().replace(/\/+$/, '');
}

function normalizeRelativePath(value: string, label: string): string {
  const trimmed = value.trim();
  if (!trimmed.startsWith('/') || ABSOLUTE_SCHEME_PATTERN.test(trimmed)) {
    throw new Error(`Expected a ${label}.`);
  }
  return trimmed;
}
