import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/client';
import { BugReportForm } from './BugReportForm';
import { todayUtc } from './dates';

function setup(onSubmit = vi.fn().mockResolvedValue(undefined), completedOn?: string) {
  const user = userEvent.setup();
  render(<BugReportForm onSubmit={onSubmit} completedOn={completedOn} />);
  const submit = () => user.click(screen.getByRole('button', { name: /report bug/i }));
  return { user, onSubmit, submit };
}

describe('BugReportForm', () => {
  it('requires a description of at least 5 characters', async () => {
    const { user, onSubmit, submit } = setup();

    await submit();
    expect(await screen.findByText('Describe the defect in at least 5 characters')).toBeInTheDocument();

    await user.type(screen.getByLabelText(/what is wrong/i), 'abc');
    await submit();
    expect(screen.getByText('Describe the defect in at least 5 characters')).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('rejects a found date before the task was completed', async () => {
    const { user, onSubmit, submit } = setup(undefined, '2026-10-23');
    await user.type(screen.getByLabelText(/what is wrong/i), 'Totals are wrong');
    const date = screen.getByLabelText(/found on/i);
    await user.clear(date);
    await user.type(date, '2026-10-17');

    await submit();

    expect(await screen.findByText('The date cannot be before the task was completed (2026-10-23)')).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('accepts any later date, even one after today', async () => {
    const { user, onSubmit, submit } = setup(undefined, '2026-10-23');
    await user.type(screen.getByLabelText(/what is wrong/i), 'Totals are wrong');
    const date = screen.getByLabelText(/found on/i);
    await user.clear(date);
    await user.type(date, '2999-01-01');

    await submit();

    expect(onSubmit).toHaveBeenCalledWith({ severity: 'Medium', description: 'Totals are wrong', foundDate: '2999-01-01' });
  });

  it('starts on the completion date when the task was completed after today', () => {
    setup(undefined, '2999-01-01');
    expect(screen.getByLabelText(/found on/i)).toHaveValue('2999-01-01');
  });

  it('submits a valid report', async () => {
    const { user, onSubmit, submit } = setup();
    await user.selectOptions(screen.getByLabelText(/severity/i), 'Critical');
    await user.type(screen.getByLabelText(/what is wrong/i), '  Card charged twice  ');

    await submit();

    expect(onSubmit).toHaveBeenCalledWith({ severity: 'Critical', description: 'Card charged twice', foundDate: todayUtc() });
  });

  it('shows a business-rule rejection from the API', async () => {
    const onSubmit = vi.fn().mockRejectedValue(new ApiError(409, 'Bugs can only be reported on Completed or Closed tasks.', 'bug.task_not_delivered'));
    const { user, submit } = setup(onSubmit);
    await user.type(screen.getByLabelText(/what is wrong/i), 'Something broke');

    await submit();

    expect(await screen.findByText(/only be reported on Completed or Closed tasks/)).toBeInTheDocument();
  });
});
