import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { DeductionPreviewLine } from '../api/types';
import { PreviewLinesTable } from './PreviewLinesTable';

const line = (overrides: Partial<DeductionPreviewLine>): DeductionPreviewLine => ({
  deductionId: 1,
  kind: 'Current',
  status: 'Approved',
  applyMonth: '2026-10',
  workDate: '2026-09-15',
  bugId: 11,
  taskId: 21,
  taskTitle: 'Checkout totals',
  moduleName: 'Cart',
  hours: 2.5,
  hourlyRate: 40,
  sharePercent: 50,
  due: 50,
  applied: 30,
  carriedForward: 20,
  ...overrides,
});

describe('PreviewLinesTable', () => {
  it('renders each line with kind, status and amounts', () => {
    render(
      <MemoryRouter>
        <PreviewLinesTable
          caption="Lines"
          emptyText="None"
          lines={[line({}), line({ deductionId: 2, kind: 'CarriedIn', applyMonth: '2026-09' }), line({ deductionId: 3, kind: 'Refund', due: -25, applied: -25, carriedForward: 0 })]}
        />
      </MemoryRouter>,
    );
    const table = screen.getByRole('table', { name: 'Lines' });
    const rows = within(table).getAllByRole('row');
    expect(rows).toHaveLength(4);
    expect(rows[1]).toHaveTextContent('Current');
    expect(rows[1]).toHaveTextContent('Approved');
    expect(rows[1]).toHaveTextContent('50%');
    expect(rows[2]).toHaveTextContent('Carried in');
    expect(rows[3]).toHaveTextContent('(refund)');
    expect(within(rows[1] as HTMLElement).getByRole('link', { name: 'Checkout totals' })).toHaveAttribute('href', '/tasks/21');
  });

  it('shows the empty text when there are no lines', () => {
    render(
      <MemoryRouter>
        <PreviewLinesTable caption="Pending" emptyText="Nothing pending." lines={[]} />
      </MemoryRouter>,
    );
    expect(screen.getByText('Nothing pending.')).toBeInTheDocument();
  });
});
