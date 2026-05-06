const KEY_TOKEN_PATTERN = /\b(F\d{1,2}|Tab|Shift|Ctrl|Alt|Enter|Esc)\b/g;

export function escapeHtml(value: string): string {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

export function formatWhatsNewBulletHtml(value: string): string {
  return escapeHtml(value).replace(
    KEY_TOKEN_PATTERN,
    '<kbd class="update-key">$1</kbd>'
  );
}
