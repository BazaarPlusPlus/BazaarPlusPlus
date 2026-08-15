import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { LocaleProvider } from '../../i18n/LocaleProvider';
import { messages } from '../../i18n/messages';
import {
  ConfirmDialog,
  requestConfirmDialogDismiss,
  type ConfirmDialogDismissReason,
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

  it('maps danger and gold tones to distinct dialog semantics', () => {
    const d = render();
    expect(d).toContain('bpp-confirm-submit is-danger');
    expect(d).toContain('bpp-confirm-tone-icon is-danger');
    expect(d).toContain('p-6 flex flex-col gap-5');
    expect(d).not.toContain('bpp-confirm-submit is-gold');
    const g = render({ tone: 'gold' });
    expect(g).toContain('bpp-confirm-submit is-gold');
    expect(g).toContain('bpp-confirm-tone-icon is-gold');
    expect(g).toContain('p-6 flex flex-col gap-6');
  });

  it('busy WITHOUT busyLabel: spinner + same label; confirm disabled', () => {
    const idle = render();
    expect(idle).toContain('animate-spin');
    expect(idle).toContain('bpp-busy-label-active" aria-hidden="true"');
    expect(idle).not.toContain('disabled=""');
    const busy = render({ busy: true });
    expect(busy).toContain('animate-spin');
    expect(busy).toContain('Confirm It');
    expect(busy).toContain('disabled=""');
    expect(busy).toContain('aria-busy="true"');
  });

  it('busy WITH busyLabel (Install affordance): text swap, NO spinner', () => {
    const busy = render({
      tone: 'gold',
      busyLabel: 'Working…',
      busy: true
    });
    expect(busy).toContain('Working…');
    expect(busy).toContain('bpp-busy-label-idle" aria-hidden="true"');
    expect(busy).toContain('Confirm It');
    expect(busy).not.toContain('animate-spin');
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

  it('blocks Escape, backdrop, close, and secondary dismissal during non-cancelable work', () => {
    const onClose = vi.fn();
    for (const reason of [
      'escape',
      'backdrop',
      'close-button',
      'secondary-action'
    ] satisfies ConfirmDialogDismissReason[]) {
      expect(
        requestConfirmDialogDismiss({
          busy: true,
          activeDismissalPolicy: { kind: 'blocked' },
          reason,
          onClose
        })
      ).toBe(false);
    }

    expect(onClose).not.toHaveBeenCalled();
    const html = render({ busy: true });
    expect(html).toContain(messages.zh.operationCannotBeCancelled);
    expect(html).not.toContain(`>${messages.zh.cancel}</button>`);
  });

  it('routes every active dismissal surface through the detachable policy', () => {
    const onClose = vi.fn();
    for (const reason of [
      'escape',
      'backdrop',
      'close-button',
      'secondary-action'
    ] satisfies ConfirmDialogDismissReason[]) {
      expect(
        requestConfirmDialogDismiss({
          busy: true,
          activeDismissalPolicy: {
            kind: 'detachable',
            label: 'Hide and continue'
          },
          reason,
          onClose
        })
      ).toBe(true);
    }

    expect(onClose).toHaveBeenCalledTimes(4);
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

  it('routes every active dismissal surface through genuine cancellation', () => {
    const onClose = vi.fn();
    const onCancel = vi.fn();
    for (const reason of [
      'escape',
      'backdrop',
      'close-button',
      'secondary-action'
    ] satisfies ConfirmDialogDismissReason[]) {
      expect(
        requestConfirmDialogDismiss({
          busy: true,
          activeDismissalPolicy: {
            kind: 'cancelable',
            label: 'Cancel operation',
            onCancel
          },
          reason,
          onClose
        })
      ).toBe(true);
    }

    expect(onCancel).toHaveBeenCalledTimes(4);
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
        reason: 'escape',
        onClose
      })
    ).toBe(true);
    expect(onClose).toHaveBeenCalledOnce();
    expect(onCancel).not.toHaveBeenCalled();
  });
});
