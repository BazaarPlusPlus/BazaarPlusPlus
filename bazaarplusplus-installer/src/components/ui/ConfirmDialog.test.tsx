import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { LocaleProvider } from '../../i18n/LocaleProvider';
import { messages } from '../../i18n/messages';
import {
  ConfirmDialog,
  requestConfirmDialogDismiss,
  type ConfirmDialogProps
} from './ConfirmDialog';

function render(overrides: Partial<ConfirmDialogProps> = {}) {
  return renderToStaticMarkup(
    <LocaleProvider>
      <ConfirmDialog
        titleId="test-title"
        title="Test Title"
        tone="danger"
        confirmLabel="Confirm It"
        busy={false}
        activeDismissalPolicy={{ kind: 'blocked' }}
        onConfirm={() => undefined}
        onClose={() => undefined}
        {...overrides}
      >
        <p>unique-body-marker</p>
      </ConfirmDialog>
    </LocaleProvider>
  );
}

describe('ConfirmDialog', () => {
  it('wires aria-labelledby <-> h2 id, title, localized close/cancel; slot order', () => {
    const html = render();
    expect(html).toContain('aria-labelledby="test-title"');
    expect(html).toContain('id="test-title"');
    expect(html).toContain(`aria-label="${messages.zh.close}"`);
    expect(html).toContain(messages.zh.cancel);
    const ti = html.indexOf('Test Title');
    const bi = html.indexOf('unique-body-marker');
    const ci = html.indexOf(messages.zh.cancel);
    const fi = html.indexOf('Confirm It');
    expect(bi).toBeGreaterThan(ti);
    expect(ci).toBeGreaterThan(bi);
    expect(fi).toBeGreaterThan(ci);
  });

  it('maps danger and primary tones to distinct confirm actions', () => {
    const d = render();
    expect(d).toContain('data-tone="danger"');
    expect(d).not.toContain('data-tone="primary"');
    const p = render({ tone: 'primary' });
    expect(p).toContain('data-tone="primary"');
  });

  it('busy WITHOUT busyLabel: spinner + same label; confirm disabled', () => {
    const idle = render();
    expect(idle).not.toContain('bpp-spin');
    expect(idle).not.toContain('disabled=""');
    const busy = render({ busy: true });
    expect(busy).toContain('bpp-spin');
    expect(busy).toContain('Confirm It');
    expect(busy).toContain('disabled=""');
    expect(busy).toContain('aria-busy="true"');
  });

  it('busy WITH busyLabel: spinner and the busy label is the visible one', () => {
    const busy = render({
      tone: 'primary',
      busyLabel: 'Working…',
      busy: true
    });
    expect(busy).toContain('Working…');
    expect(busy).toContain('bpp-spin');
    expect(busy).toContain('aria-busy="true"');
    expect(busy).toContain('disabled=""');
  });

  it('acknowledge gates confirm and renders tone-styled checkbox', () => {
    const ack = (checked: boolean) =>
      render({
        acknowledge: {
          label: 'ack-label',
          checked,
          onChange: () => undefined
        }
      });
    const off = ack(false);
    expect(off).toContain('type="checkbox"');
    expect(off).toContain('ack-label');
    expect(off).toContain('disabled=""');
    expect(off).not.toContain('checked=""');
    const on = ack(true);
    expect(on).toContain('checked=""');
    expect(on).not.toContain('disabled=""');
  });

  it('confirmDisabled disables confirm even when idle (Cleanup nothing-to-clean)', () => {
    expect(render({ confirmDisabled: true })).toContain('disabled=""');
  });

  it('blocks dismissal during non-cancelable work', () => {
    const onClose = vi.fn();
    expect(
      requestConfirmDialogDismiss({
        busy: true,
        activeDismissalPolicy: { kind: 'blocked' },
        onClose
      })
    ).toBe(false);

    expect(onClose).not.toHaveBeenCalled();
    const html = render({ busy: true });
    expect(html).toContain(messages.zh.operationCannotBeCancelled);
    expect(html).not.toContain(`>${messages.zh.cancel}</button>`);
  });

  it('routes active dismissal through the detachable policy', () => {
    const onClose = vi.fn();
    expect(
      requestConfirmDialogDismiss({
        busy: true,
        activeDismissalPolicy: {
          kind: 'detachable',
          label: 'Hide and continue'
        },
        onClose
      })
    ).toBe(true);

    expect(onClose).toHaveBeenCalledOnce();
    expect(
      render({
        busy: true,
        activeDismissalPolicy: {
          kind: 'detachable',
          label: 'Hide and continue'
        }
      })
    ).toContain('Hide and continue');
  });

  it('routes active dismissal through genuine cancellation', () => {
    const onClose = vi.fn();
    const onCancel = vi.fn();
    expect(
      requestConfirmDialogDismiss({
        busy: true,
        activeDismissalPolicy: {
          kind: 'cancelable',
          label: 'Cancel operation',
          onCancel
        },
        onClose
      })
    ).toBe(true);

    expect(onCancel).toHaveBeenCalledOnce();
    expect(onClose).not.toHaveBeenCalled();
  });

  it('uses ordinary close semantics before an operation starts', () => {
    const onClose = vi.fn();
    const onCancel = vi.fn();
    expect(
      requestConfirmDialogDismiss({
        busy: false,
        activeDismissalPolicy: {
          kind: 'cancelable',
          label: 'Cancel operation',
          onCancel
        },
        onClose
      })
    ).toBe(true);
    expect(onClose).toHaveBeenCalledOnce();
    expect(onCancel).not.toHaveBeenCalled();
  });
});
