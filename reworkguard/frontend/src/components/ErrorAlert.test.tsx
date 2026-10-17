import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/client';
import { ErrorAlert } from './ErrorAlert';

describe('ErrorAlert', () => {
  it('shows the problem code and offers a reload for stale payroll runs', async () => {
    const onReload = vi.fn();
    render(<ErrorAlert error={new ApiError(409, 'The run has changed since you loaded it.', 'payroll.stale')} onReload={onReload} />);
    expect(screen.getByRole('alert')).toHaveTextContent('The run has changed since you loaded it.');
    expect(screen.getByText('(payroll.stale)')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Reload latest' }));
    expect(onReload).toHaveBeenCalled();
  });

  it('titles other conflicts without a reload button', () => {
    render(<ErrorAlert error={new ApiError(409, 'The approver cannot be the reviewer.', 'payroll.segregation')} />);
    expect(screen.getByText('Not allowed right now')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});
