import { Link, Paper, Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import type { PayrollRunItem } from '../api/types';
import { formatMoney, humanize } from './format';

export function RunItemsTable({ items, caption }: { items: PayrollRunItem[]; caption: string }) {
  return (
    <TableContainer component={Paper} variant="outlined">
      <Table size="small" aria-label={caption}>
        <TableHead>
          <TableRow>
            <TableCell>Line</TableCell>
            <TableCell>Kind</TableCell>
            <TableCell>Original month</TableCell>
            <TableCell>Work date</TableCell>
            <TableCell>Task / bug</TableCell>
            <TableCell align="right">Due</TableCell>
            <TableCell align="right">Applied</TableCell>
            <TableCell align="right">Carried forward</TableCell>
            <TableCell align="right">Settled manually</TableCell>
            <TableCell align="right">Cancelled by refund</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {items.map((i) => (
            <TableRow key={`${i.deductionId}-${i.kind}`}>
              <TableCell>#{i.deductionId}</TableCell>
              <TableCell>{i.kind === 'CarriedIn' ? 'Carried in' : humanize(i.kind)}</TableCell>
              <TableCell>{i.originalApplyMonth}</TableCell>
              <TableCell>{i.workDate}</TableCell>
              <TableCell>
                <Link component={RouterLink} to={`/tasks/${i.taskId}`}>
                  {i.taskTitle}
                </Link>{' '}
                · bug #{i.bugId}
              </TableCell>
              <TableCell align="right">{formatMoney(i.due)}</TableCell>
              <TableCell align="right">{formatMoney(i.applied)}</TableCell>
              <TableCell align="right">{formatMoney(i.carriedForward)}</TableCell>
              <TableCell align="right">{formatMoney(i.settledManually)}</TableCell>
              <TableCell align="right">{formatMoney(i.cancelledByCompensation)}</TableCell>
            </TableRow>
          ))}
          {items.length === 0 && (
            <TableRow>
              <TableCell colSpan={10}>No lines.</TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
