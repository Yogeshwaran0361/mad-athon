import { screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { DeductionPreview } from '../api/types';
import { mockFetch, renderWithProviders } from '../test/utils';
import { DeductionPreviewPage } from './DeductionPreviewPage';

afterEach(() => {
  vi.unstubAllGlobals();
});

const preview: DeductionPreview = {
  employeeId: 1,
  employeeName: 'Employee User',
  payMode: 'Monthly',
  periodMonth: '2026-10',
  source: 'Estimate',
  payrollRunId: null,
  salaryBase: 5000,
  salaryBasis: 'Monthly gross',
  capPercent: 10,
  capAmount: 500,
  carriedIn: 20,
  currentDue: 80,
  refunds: 0,
  applied: 100,
  carriedForward: 0,
  settledManually: 0,
  netAdjustment: -100,
  lines: [],
  pending: [
    {
      deductionId: 9,
      kind: 'Current',
      status: 'Notified',
      applyMonth: '2026-10',
      workDate: '2026-10-02',
      bugId: 3,
      taskId: 4,
      taskTitle: 'Login fix',
      moduleName: 'Auth',
      hours: 1,
      hourlyRate: 30,
      sharePercent: 100,
      due: 30,
      applied: 0,
      carriedForward: 0,
    },
  ],
  earnings: 4800,
  netPay: 9700,
  earningLines: [{ awardId: 5, changeRequestId: 2, changeRequestTitle: 'Bulk export', hours: 60, sharePercent: 60, amount: 4800 }],
};

describe('DeductionPreviewPage', () => {
  it('requests the new preview endpoint without a month and shows figures and pending lines', async () => {
    const fetch = mockFetch({ '/api/v1/deductions/preview': preview });
    renderWithProviders(<DeductionPreviewPage />);
    expect(await screen.findByText('Cap (10%)')).toBeInTheDocument();
    expect(fetch.mock.calls[0]?.[0]).toBe('/api/v1/deductions/preview?employeeId=1');
    expect(screen.getByRole('table', { name: 'Pending deduction lines' })).toHaveTextContent('Login fix');
    expect(screen.getByText('No deductions for this month.')).toBeInTheDocument();
    expect(screen.getByText(/source:/)).toHaveTextContent('Estimate');
  });

  it('shows the profit share as its own earning line between gross and deductions', async () => {
    mockFetch({ '/api/v1/deductions/preview': preview });
    renderWithProviders(<DeductionPreviewPage />);
    const breakdown = await screen.findByRole('table', { name: 'Pay breakdown' });
    expect(breakdown).toHaveTextContent(/Gross.*Enhancement profit share: Bulk export.*60\.00 h · share 60\.00%.*Rework deductions.*= Net/);
  });
});
