import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { StatusBanner } from './StatusBanner';

describe('StatusBanner', () => {
  it('announces errors assertively as alerts', () => {
    const html = renderToStaticMarkup(
      <StatusBanner tone="error" message="Something failed" />
    );

    expect(html).toContain('role="alert"');
    expect(html).toContain('aria-live="assertive"');
    expect(html).toContain('Something failed');
  });

  it('announces non-error feedback politely as status', () => {
    const html = renderToStaticMarkup(
      <StatusBanner tone="success" message="Saved" />
    );

    expect(html).toContain('role="status"');
    expect(html).toContain('aria-live="polite"');
    expect(html).toContain('Saved');
  });

  it('renders actions and an optional diagnostic disclosure', () => {
    const html = renderToStaticMarkup(
      <StatusBanner
        tone="warning"
        message="Native data is unavailable"
        actions={<button type="button">Retry</button>}
        diagnostic="native timeout"
        diagnosticLabel="Technical details"
      />
    );

    expect(html).toContain('Retry');
    expect(html).toContain('<details');
    expect(html).toContain('Technical details');
    expect(html).toContain('native timeout');
  });
});
