// @vitest-environment jsdom

// Behaviour of the shared Web component library (design/ui), which the
// installer and the site both render.
import { act, StrictMode, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { renderToStaticMarkup } from 'react-dom/server';
import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  vi
} from 'vite-plus/test';
import { Badge, Button, Dialog, StatusPanel } from '@bpp/ui';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  // jsdom has no modal dialog; model only the open flag the component reads.
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', {
    configurable: true,
    value: function (this: HTMLDialogElement) {
      this.open = true;
    }
  });
  Object.defineProperty(HTMLDialogElement.prototype, 'close', {
    configurable: true,
    value: function (this: HTMLDialogElement) {
      this.open = false;
    }
  });
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'close');
  vi.unstubAllGlobals();
});

/** Escape on a modal dialog fires `cancel`. */
function escape() {
  container
    .querySelector('dialog')
    ?.dispatchEvent(new Event('cancel', { cancelable: true }));
}

async function render(node: ReactNode) {
  await act(async () => root.render(<StrictMode>{node}</StrictMode>));
}

describe('Button', () => {
  it('blocks a busy button and names it busy', () => {
    const html = renderToStaticMarkup(<Button busy>Install</Button>);
    expect(html).toContain('disabled=""');
    expect(html).toContain('aria-busy="true"');
    expect(html).toContain('type="button"');
  });

  it('keeps a disabled link out of the tab order and ignores its clicks', async () => {
    const onClick = vi.fn();
    await render(
      <Button href="https://example.com" disabled onClick={onClick}>
        Open
      </Button>
    );
    const link = container.querySelector('a');
    expect(link?.getAttribute('aria-disabled')).toBe('true');
    expect(link?.getAttribute('tabindex')).toBe('-1');
    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    await act(async () => link?.dispatchEvent(event));
    expect(onClick).not.toHaveBeenCalled();
    expect(event.defaultPrevented).toBe(true);
  });
});

describe('Badge', () => {
  it('reads as dot + word unless the dot is turned off', () => {
    expect(renderToStaticMarkup(<Badge tone="ok">Ready</Badge>)).toBe(
      '<span class="bpp-badge bpp-badge-ok">Ready</span>'
    );
    expect(renderToStaticMarkup(<Badge dot={false}>EU</Badge>)).toContain(
      'no-dot'
    );
  });
});

describe('StatusPanel', () => {
  it('titles the panel at the requested level, a page title at level 1', () => {
    expect(
      renderToStaticMarkup(<StatusPanel kind="empty" title="Nothing" />)
    ).toContain('<h2 class="bpp-status-panel-title">Nothing</h2>');
    expect(
      renderToStaticMarkup(
        <StatusPanel kind="not-found" title="Lost" headingLevel={1} />
      )
    ).toContain('<h1 class="bpp-status-panel-title is-page">Lost</h1>');
  });

  it('names the loading mark as a progress bar when given a label', () => {
    const html = renderToStaticMarkup(
      <StatusPanel
        kind="loading"
        title="Loading"
        progressLabel="Loading data"
      />
    );
    expect(html).toContain('role="progressbar"');
    expect(html).toContain('aria-label="Loading data"');
  });
});

describe('Dialog', () => {
  it('locks page scroll while open and returns focus to its opener', async () => {
    const opener = document.createElement('button');
    document.body.append(opener);
    opener.focus();

    await render(
      <Dialog onClose={() => undefined} labelledBy="t">
        <button type="button">Inside</button>
      </Dialog>
    );
    expect(container.querySelector('dialog')?.open).toBe(true);
    expect(document.body.style.overflow).toBe('hidden');
    container.querySelector<HTMLButtonElement>('dialog button')?.focus();

    await act(async () => root.render(<StrictMode>{null}</StrictMode>));
    expect(document.body.style.overflow).toBe('');
    expect(document.activeElement).toBe(opener);
    opener.remove();
  });

  it('closes on Escape unless dismissal is off', async () => {
    const onClose = vi.fn();

    await render(
      <Dialog onClose={onClose} dismissible={false}>
        <p>Busy</p>
      </Dialog>
    );
    await act(async () => escape());
    expect(onClose).not.toHaveBeenCalled();

    await render(
      <Dialog onClose={onClose}>
        <p>Idle</p>
      </Dialog>
    );
    await act(async () => escape());
    expect(onClose).toHaveBeenCalledOnce();
  });
});
