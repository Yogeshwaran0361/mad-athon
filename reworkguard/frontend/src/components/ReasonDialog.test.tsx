import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ReasonDialog } from './ReasonDialog';

describe('ReasonDialog', () => {
  it('requires the minimum reason length before confirming', async () => {
    const onConfirm = vi.fn();
    render(<ReasonDialog open title="Dispute line #4" label="Reason" minLength={10} confirmLabel="Raise dispute" onClose={vi.fn()} onConfirm={onConfirm} />);
    const dialog = screen.getByRole('dialog', { name: 'Dispute line #4' });
    const field = screen.getByLabelText(/reason/i);
    expect(field).toHaveFocus();
    await userEvent.type(field, 'too short');
    await userEvent.click(screen.getByRole('button', { name: 'Raise dispute' }));
    expect(onConfirm).not.toHaveBeenCalled();
    expect(field).toHaveAttribute('aria-invalid', 'true');
    await userEvent.type(field, ' but now long enough');
    await userEvent.click(screen.getByRole('button', { name: 'Raise dispute' }));
    expect(onConfirm).toHaveBeenCalledWith('too short but now long enough');
    expect(dialog).toBeInTheDocument();
  });
});
