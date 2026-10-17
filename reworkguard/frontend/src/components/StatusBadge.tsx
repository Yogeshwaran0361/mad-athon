import { Chip, type ChipProps } from '@mui/material';
import type { ApprovalStatus, BugStatus, DeductionStatus, PayrollRunStatus, WorkTaskStatus } from '../api/types';

const taskColors: Record<WorkTaskStatus, ChipProps['color']> = {
  Assigned: 'default',
  InProgress: 'info',
  Completed: 'success',
  Reopened: 'error',
  UnderFix: 'warning',
  Closed: 'secondary',
};

const bugColors: Record<BugStatus, ChipProps['color']> = {
  Open: 'error',
  Fixed: 'warning',
  Verified: 'success',
  Closed: 'secondary',
};

const approvalColors: Record<ApprovalStatus, ChipProps['color']> = {
  Pending: 'warning',
  Approved: 'success',
  Rejected: 'error',
};

const labels: Partial<Record<string, string>> = { InProgress: 'In progress', UnderFix: 'Under fix' };

export function TaskStatusBadge({ status }: { status: WorkTaskStatus }) {
  return <Chip size="small" label={labels[status] ?? status} color={taskColors[status]} />;
}

export function BugStatusBadge({ status }: { status: BugStatus }) {
  return <Chip size="small" variant="outlined" label={status} color={bugColors[status]} />;
}

export function ApprovalBadge({ status }: { status: ApprovalStatus }) {
  return <Chip size="small" variant="outlined" label={status} color={approvalColors[status]} />;
}

const deductionColors: Record<DeductionStatus, ChipProps['color']> = {
  Draft: 'default',
  Notified: 'info',
  Disputed: 'error',
  Resolved: 'warning',
  Approved: 'primary',
  Locked: 'secondary',
  Applied: 'success',
  Cancelled: 'default',
  Reversed: 'default',
};

const runColors: Record<PayrollRunStatus, ChipProps['color']> = {
  Preview: 'default',
  Reviewed: 'info',
  Approved: 'primary',
  Finalized: 'warning',
  Locked: 'success',
  Superseded: 'default',
};

export function DeductionStatusBadge({ status }: { status: DeductionStatus }) {
  return <Chip size="small" variant="outlined" label={status} color={deductionColors[status]} />;
}

export function PayrollRunStatusBadge({ status }: { status: PayrollRunStatus }) {
  return <Chip size="small" label={status} color={runColors[status]} variant={status === 'Superseded' ? 'outlined' : 'filled'} />;
}
