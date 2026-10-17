import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { PayrollRunSummary } from '../api/types';
import { PayrollStepButtons } from './PayrollStepButtons';

function run(overrides: Partial<PayrollRunSummary> = {}): PayrollRunSummary {
  return {
    id: 7,
    periodMonth: '2026-09',
    runVersion: 2,
    status: 'Preview',
    inputsHash: 'abc',
    capPercent: 10,
    employeeCount: 3,
    totalApplied: 100,
    totalCarriedForward: 0,
    totalRefunds: 0,
    totalSettledManually: 0,
    generatedByEmployeeId: 50,
    generatedAtUtc: '2026-10-01T00:00:00Z',
    reviewedByEmployeeId: null,
    reviewedAtUtc: null,
    approvedByEmployeeId: null,
    approvedAtUtc: null,
    finalizedByEmployeeId: null,
    finalizedAtUtc: null,
    lockedByEmployeeId: null,
    lockedAtUtc: null,
    supersededAtUtc: null,
    version: 4,
    ...overrides,
  };
}

describe('PayrollStepButtons', () => {
  it('lets Finance review a preview and sends the viewed version', async () => {
    const onStep = vi.fn();
    render(<PayrollStepButtons run={run()} role="Finance" userId={1} busy={false} onStep={onStep} />);
    await userEvent.click(screen.getByRole('button', { name: 'Mark reviewed' }));
    expect(onStep).toHaveBeenCalledWith('review', 4);
  });

  it('tells a Manager they are waiting for Finance on a preview', () => {
    render(<PayrollStepButtons run={run()} role="Manager" userId={1} busy={false} onStep={vi.fn()} />);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(screen.getByText(/waiting for finance/i)).toBeInTheDocument();
  });

  it('lets a different Manager approve a reviewed run', async () => {
    const onStep = vi.fn();
    render(<PayrollStepButtons run={run({ status: 'Reviewed', reviewedByEmployeeId: 51 })} role="Manager" userId={9} busy={false} onStep={onStep} />);
    await userEvent.click(screen.getByRole('button', { name: 'Approve' }));
    expect(onStep).toHaveBeenCalledWith('approve', 4);
  });

  it('blocks approval by the generator', () => {
    render(<PayrollStepButtons run={run({ status: 'Reviewed' })} role="Manager" userId={50} busy={false} onStep={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Approve' })).toBeDisabled();
    expect(screen.getByText(/another manager must approve/i)).toBeInTheDocument();
  });

  it('asks for confirmation before locking', async () => {
    const onStep = vi.fn();
    render(<PayrollStepButtons run={run({ status: 'Finalized' })} role="Finance" userId={1} busy={false} onStep={onStep} />);
    await userEvent.click(screen.getByRole('button', { name: 'Lock & apply' }));
    expect(onStep).not.toHaveBeenCalled();
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent(/cannot be undone/i);
    await userEvent.click(screen.getAllByRole('button', { name: 'Lock & apply' }).at(-1) as HTMLElement);
    expect(onStep).toHaveBeenCalledWith('lock', 4);
  });

  it('shows no actions on a locked run', () => {
    render(<PayrollStepButtons run={run({ status: 'Locked' })} role="Finance" userId={1} busy={false} onStep={vi.fn()} />);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(screen.getByText(/locked and applied/i)).toBeInTheDocument();
  });
});
