import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, test, vi } from 'vitest';

import DialogShell from '../src/shared/components/DialogShell';

describe('DialogShell', () => {
  test('renders nothing when closed', () => {
    render(
      <DialogShell open={false} onClose={() => {}} labelledBy="t" closeLabel="Close">
        <h2 id="t">Hidden</h2>
      </DialogShell>
    );

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  test('renders dialog and locks body scroll when open', () => {
    render(
      <DialogShell open onClose={() => {}} labelledBy="t" closeLabel="Close">
        <h2 id="t">Visible</h2>
      </DialogShell>
    );

    expect(screen.getByRole('dialog', { name: 'Visible' })).toBeInTheDocument();
    expect(document.body.style.overflow).toBe('hidden');
  });

  test('calls onClose when Escape is pressed', () => {
    const onClose = vi.fn();

    render(
      <DialogShell open onClose={onClose} labelledBy="t" closeLabel="Close">
        <h2 id="t">Visible</h2>
      </DialogShell>
    );

    fireEvent.keyDown(document, { key: 'Escape' });

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  test('calls onClose when the backdrop or close button is clicked', () => {
    const onClose = vi.fn();

    render(
      <DialogShell open onClose={onClose} labelledBy="t" closeLabel="Close dialog">
        <h2 id="t">Visible</h2>
      </DialogShell>
    );

    const closeButtons = screen.getAllByRole('button', { name: 'Close dialog' });
    expect(closeButtons.length).toBeGreaterThanOrEqual(2);

    fireEvent.click(closeButtons[0]);
    fireEvent.click(closeButtons[1]);

    expect(onClose).toHaveBeenCalledTimes(2);
  });
});
