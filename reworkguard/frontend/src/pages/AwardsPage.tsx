import { useState } from 'react';
import {
  Button,
  CircularProgress,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useDecideAward, useIncentiveAwards } from '../api/hooks';
import { useAuth } from '../auth/AuthContext';
import { ErrorAlert } from '../components/ErrorAlert';
import { formatMoney } from '../components/format';
import { ReasonDialog } from '../components/ReasonDialog';

/** Profit-share awards. Managers approve or reject others' awards; everyone else sees only their own. */
export function AwardsPage() {
  const { user, hasRole } = useAuth();
  const { data, isLoading, error } = useIncentiveAwards();
  const decide = useDecideAward();
  const [rejecting, setRejecting] = useState<number | null>(null);
  const isManager = hasRole('Manager');

  return (
    <Stack spacing={2}>
      <Typography variant="h5">Profit-share awards</Typography>
      <Typography variant="body2" color="text.secondary">
        Shares of the profit on closed feature requests. Approved awards are paid as a separate earning line in the next payroll.
      </Typography>
      <ErrorAlert error={error ?? decide.error} />
      {isLoading && <CircularProgress />}
      {data && (
        <TableContainer component={Paper}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Feature request</TableCell>
                <TableCell>Employee</TableCell>
                <TableCell align="right">Hours</TableCell>
                <TableCell align="right">Share</TableCell>
                <TableCell align="right">Amount</TableCell>
                <TableCell>Pay month</TableCell>
                <TableCell>Status</TableCell>
                <TableCell align="right">Decision</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.map((a) => (
                <TableRow key={a.id}>
                  <TableCell>{a.changeRequestTitle}</TableCell>
                  <TableCell>{a.employeeName}</TableCell>
                  <TableCell align="right">{a.hours.toFixed(2)}</TableCell>
                  <TableCell align="right">{a.sharePercent.toFixed(2)}%</TableCell>
                  <TableCell align="right">{formatMoney(a.amount)}</TableCell>
                  <TableCell>{a.applyMonth}</TableCell>
                  <TableCell>{a.status}{a.rejectionReason ? `: ${a.rejectionReason}` : ''}</TableCell>
                  <TableCell align="right">
                    {isManager && a.status === 'Pending' && a.employeeId !== user?.id && (
                      <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
                        <Button size="small" variant="contained" disabled={decide.isPending} onClick={() => { decide.mutate({ id: a.id, decision: 'Approved' }); }}>
                          Approve
                        </Button>
                        <Button size="small" color="error" disabled={decide.isPending} onClick={() => { setRejecting(a.id); }}>
                          Reject
                        </Button>
                      </Stack>
                    )}
                  </TableCell>
                </TableRow>
              ))}
              {data.length === 0 && (
                <TableRow>
                  <TableCell colSpan={8}>No awards.</TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      )}
      {rejecting !== null && (
        <ReasonDialog
          open
          title="Reject award"
          label="Reason"
          minLength={1}
          confirmLabel="Reject"
          onClose={() => { setRejecting(null); }}
          onConfirm={(reason) => {
            decide.mutate({ id: rejecting, decision: 'Rejected', reason });
            setRejecting(null);
          }}
        />
      )}
    </Stack>
  );
}
