import { Checkbox, Link, Paper, Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material';
import type { ReactNode } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import type { Deduction } from '../api/types';
import { formatHours, formatMoney, formatPercent, formatUtc } from './format';
import { DeductionStatusBadge } from './StatusBadge';

interface Props {
  rows: Deduction[];
  caption: string;
  showEmployee?: boolean;
  selected?: Set<number>;
  onToggle?: (id: number) => void;
  onToggleAll?: (checked: boolean) => void;
  actions?: (row: Deduction) => ReactNode;
  emptyText: string;
}

export function DeductionTable({ rows, caption, showEmployee = true, selected, onToggle, onToggleAll, actions, emptyText }: Props) {
  const selectable = selected !== undefined && onToggle !== undefined;
  const allSelected = selectable && rows.length > 0 && rows.every((r) => selected.has(r.id));
  const columns = 9 + (showEmployee ? 1 : 0) + (selectable ? 1 : 0) + (actions ? 1 : 0);
  return (
    <TableContainer component={Paper} variant="outlined">
      <Table size="small" aria-label={caption}>
        <TableHead>
          <TableRow>
            {selectable && (
              <TableCell padding="checkbox">
                <Checkbox
                  checked={allSelected}
                  indeterminate={!allSelected && rows.some((r) => selected.has(r.id))}
                  onChange={(e) => onToggleAll?.(e.target.checked)}
                  slotProps={{ input: { 'aria-label': 'Select all lines' } }}
                />
              </TableCell>
            )}
            <TableCell>#</TableCell>
            {showEmployee && <TableCell>Employee</TableCell>}
            <TableCell>Status</TableCell>
            <TableCell>Task / bug</TableCell>
            <TableCell>Work date</TableCell>
            <TableCell>Apply month</TableCell>
            <TableCell align="right">Hours</TableCell>
            <TableCell align="right">Share</TableCell>
            <TableCell align="right">Amount</TableCell>
            <TableCell>Dispute window</TableCell>
            {actions && <TableCell align="right">Actions</TableCell>}
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map((d) => (
            <TableRow key={d.id} selected={selected?.has(d.id)}>
              {selectable && (
                <TableCell padding="checkbox">
                  <Checkbox
                    checked={selected.has(d.id)}
                    onChange={() => {
                      onToggle(d.id);
                    }}
                    slotProps={{ input: { 'aria-label': `Select line ${d.id}` } }}
                  />
                </TableCell>
              )}
              <TableCell>{d.id}</TableCell>
              {showEmployee && <TableCell>{d.employeeName}</TableCell>}
              <TableCell>
                <DeductionStatusBadge status={d.status} />
                {d.kind === 'Compensating' && ' refund'}
              </TableCell>
              <TableCell>
                <Link component={RouterLink} to={`/tasks/${d.taskId}`}>
                  {d.taskTitle}
                </Link>{' '}
                · bug #{d.bugId}
                {d.dispute && (
                  <div>
                    <small>
                      Dispute: {d.dispute.reason}
                      {d.dispute.outcome && ` → ${d.dispute.outcome}: ${d.dispute.resolutionJustification ?? ''}`}
                    </small>
                  </div>
                )}
              </TableCell>
              <TableCell>{d.workDate}</TableCell>
              <TableCell>{d.applyMonth}</TableCell>
              <TableCell align="right">{formatHours(d.hours)}</TableCell>
              <TableCell align="right">{formatPercent(d.sharePercent)}</TableCell>
              <TableCell align="right">{formatMoney(d.amount)}</TableCell>
              <TableCell>{d.disputeWindowEndsAtUtc ? `${d.disputeWindowOpen ? 'Open until' : 'Closed'} ${formatUtc(d.disputeWindowEndsAtUtc)}` : '–'}</TableCell>
              {actions && <TableCell align="right">{actions(d)}</TableCell>}
            </TableRow>
          ))}
          {rows.length === 0 && (
            <TableRow>
              <TableCell colSpan={columns}>{emptyText}</TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
