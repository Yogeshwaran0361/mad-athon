import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { WorkType } from '../api/types';
import { ApiError } from '../api/client';
import { TimeLogForm } from './TimeLogForm';
import { todayUtc } from './dates';

const bugs = [
  { id: 7, description: 'Declined cards are charged' },
  { id: 9, description: 'Receipt shows wrong total' },
];

function setup(allowedTypes: WorkType[] = ['Corrective'], onSubmit = vi.fn().mockResolvedValue(undefined)) {
  const user = userEvent.setup();
  render(<TimeLogForm taskId={42} allowedTypes={allowedTypes} openBugs={bugs} onSubmit={onSubmit} />);
  const submit = () => user.click(screen.getByRole('button', { name: /log time/i }));
  return { user, onSubmit, submit };
}

describe('TimeLogForm', () => {
  it.each([
    ['', 'Enter the hours worked'],
    ['0', 'Hours must be more than 0'],
    ['24.5', 'At most 24 hours in a day'],
    ['1.234', 'Use a number with at most two decimals'],
    ['abc', 'Use a number with at most two decimals'],
  ])('rejects hours "%s"', async (hours, message) => {
    const { user, onSubmit, submit } = setup();
    await user.selectOptions(screen.getByLabelText(/bug being fixed/i), '7');
    if (hours) {
      await user.type(screen.getByLabelText(/^hours/i), hours);
    }

    await submit();

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('requires a bug for corrective hours', async () => {
    const { user, onSubmit, submit } = setup();
    await user.type(screen.getByLabelText(/^hours/i), '2');

    await submit();

    expect(await screen.findByText('Pick the bug you are fixing')).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('submits corrective hours tagged with the bug', async () => {
    const { user, onSubmit, submit } = setup();
    await user.selectOptions(screen.getByLabelText(/bug being fixed/i), '9');
    await user.type(screen.getByLabelText(/^hours/i), '3.5');
    await user.type(screen.getByLabelText(/note/i), 'Fixed rounding');

    await submit();

    expect(onSubmit).toHaveBeenCalledWith({
      taskId: 42,
      workType: 'Corrective',
      bugId: 9,
      workDate: todayUtc(),
      hours: 3.5,
      note: 'Fixed rounding',
    });
  });

  it('only offers regular hours when the task accepts them, with no bug field', async () => {
    const { user, onSubmit, submit } = setup(['Regular']);
    expect(screen.queryByLabelText(/bug being fixed/i)).not.toBeInTheDocument();
    await user.type(screen.getByLabelText(/^hours/i), '8');

    await submit();

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ workType: 'Regular', bugId: null, hours: 8, note: null }));
  });

  it('shows API field errors next to the field', async () => {
    const onSubmit = vi.fn().mockRejectedValue(new ApiError(400, 'Validation failed', undefined, { Hours: ['Hours can have at most two decimals.'] }));
    const { user, submit } = setup(['Regular'], onSubmit);
    await user.type(screen.getByLabelText(/^hours/i), '2');

    await submit();

    expect(await screen.findByText('Hours can have at most two decimals.')).toBeInTheDocument();
  });

  it('shows the daily-limit conflict from the API', async () => {
    const onSubmit = vi.fn().mockRejectedValue(new ApiError(409, 'This would bring your total to 25 hours; the limit is 24.', 'timelog.daily_limit'));
    const { user, submit } = setup(['Regular'], onSubmit);
    await user.type(screen.getByLabelText(/^hours/i), '2');

    await submit();

    expect(await screen.findByText(/the limit is 24/)).toBeInTheDocument();
  });
});
