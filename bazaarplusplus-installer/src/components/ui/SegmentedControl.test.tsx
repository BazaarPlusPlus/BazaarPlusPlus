import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { SegmentedControl } from './SegmentedControl';

describe('SegmentedControl', () => {
  it('labels the radio group and exposes option, checked, and disabled semantics', () => {
    const html = renderToStaticMarkup(
      <SegmentedControl
        label="Display mode"
        name="display-mode"
        value="full"
        options={[
          { value: 'count', label: 'Battle Count' },
          { value: 'full', label: 'Full Hero' },
          { value: 'half', label: 'Half Hero', disabled: true }
        ]}
        onChange={() => undefined}
      />
    );

    expect(html).toContain('role="radiogroup"');
    expect(html).toContain('aria-label="Display mode"');
    expect(html).toContain('name="display-mode"');
    expect(html).toContain('value="count"');
    expect(html).toContain('Battle Count');
    const fullOption = html.match(/<input[^>]+value="full"[^>]*>/)?.[0];
    expect(fullOption).toContain('checked=""');
    expect(html).toContain('Full Hero');
    const halfOption = html.match(/<input[^>]+value="half"[^>]*>/)?.[0];
    expect(halfOption).toContain('disabled=""');
    expect(html).toContain('Half Hero');
  });
});
