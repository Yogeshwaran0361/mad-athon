import { Paper, Table, TableBody, TableCell, TableRow, Typography } from '@mui/material';
import type { EarningLine } from '../api/types';
import { formatMoney } from './format';

interface Props {
  gross: number;
  earningLines: EarningLine[];
  applied: number;
  refunds: number;
  net: number;
}

/** Gross, + profit-share earnings, − rework deductions, = net. Earnings and deductions stay separate lines. */
export function PayBreakdown({ gross, earningLines, applied, refunds, net }: Props) {
  return (
    <Paper variant="outlined" sx={{ maxWidth: 560 }}>
      <Table size="small" aria-label="Pay breakdown">
        <TableBody>
          <TableRow>
            <TableCell>Gross (salary base)</TableCell>
            <TableCell align="right">{formatMoney(gross)}</TableCell>
          </TableRow>
          {earningLines.map((e) => (
            <TableRow key={e.awardId}>
              <TableCell>
                + Enhancement profit share: {e.changeRequestTitle}
                <Typography variant="caption" display="block" color="text.secondary">
                  {e.hours.toFixed(2)} h · share {e.sharePercent.toFixed(2)}%
                </Typography>
              </TableCell>
              <TableCell align="right">{formatMoney(e.amount)}</TableCell>
            </TableRow>
          ))}
          <TableRow>
            <TableCell>− Rework deductions</TableCell>
            <TableCell align="right">{formatMoney(applied)}</TableCell>
          </TableRow>
          {refunds !== 0 && (
            <TableRow>
              <TableCell>+ Refunds</TableCell>
              <TableCell align="right">{formatMoney(-refunds)}</TableCell>
            </TableRow>
          )}
          <TableRow>
            <TableCell sx={{ fontWeight: 600 }}>= Net</TableCell>
            <TableCell align="right" sx={{ fontWeight: 600 }}>{formatMoney(net)}</TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Paper>
  );
}
