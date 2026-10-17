import { Link, Paper, Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import type { DeductionPreviewLine } from '../api/types';
import { formatHours, formatMoney, formatPercent, humanize } from './format';
import { DeductionStatusBadge } from './StatusBadge';

/** Lines of a deduction preview. Refund amounts are negative and labelled, not only coloured. */
export function PreviewLinesTable({ lines, caption, emptyText }: { lines: DeductionPreviewLine[]; caption: string; emptyText: string }) {
  return (
    <TableContainer component={Paper} variant="outlined">
      <Table size="small" aria-label={caption}>
        <TableHead>
          <TableRow>
            <TableCell>Kind</TableCell>
            <TableCell>Status</TableCell>
            <TableCell>Apply month</TableCell>
            <TableCell>Work date</TableCell>
            <TableCell>Task / bug</TableCell>
            <TableCell>Module</TableCell>
            <TableCell align="right">Hours</TableCell>
            <TableCell align="right">Rate</TableCell>
            <TableCell align="right">Share</TableCell>
            <TableCell align="right">Due</TableCell>
            <TableCell align="right">Applied</TableCell>
            <TableCell align="right">Carried forward</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {lines.map((line) => (
            <TableRow key={`${line.deductionId}-${line.kind}`}>
              <TableCell>{line.kind === 'CarriedIn' ? 'Carried in' : humanize(line.kind)}</TableCell>
              <TableCell>
                <DeductionStatusBadge status={line.status} />
              </TableCell>
              <TableCell>{line.applyMonth}</TableCell>
              <TableCell>{line.workDate}</TableCell>
              <TableCell>
                <Link component={RouterLink} to={`/tasks/${line.taskId}`}>
                  {line.taskTitle}
                </Link>{' '}
                · bug #{line.bugId}
              </TableCell>
              <TableCell>{line.moduleName}</TableCell>
              <TableCell align="right">{formatHours(line.hours)}</TableCell>
              <TableCell align="right">{formatMoney(line.hourlyRate)}</TableCell>
              <TableCell align="right">{formatPercent(line.sharePercent)}</TableCell>
              <TableCell align="right">
                {formatMoney(line.due)}
                {line.due < 0 && ' (refund)'}
              </TableCell>
              <TableCell align="right">{formatMoney(line.applied)}</TableCell>
              <TableCell align="right">{formatMoney(line.carriedForward)}</TableCell>
            </TableRow>
          ))}
          {lines.length === 0 && (
            <TableRow>
              <TableCell colSpan={12}>{emptyText}</TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
