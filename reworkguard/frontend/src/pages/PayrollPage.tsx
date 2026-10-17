import { useState } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import { Alert, Button, CircularProgress, Link, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { useGeneratePayrollRun, usePayrollPeriods, usePayrollRuns } from '../api/hooks';
import { useAuth } from '../auth/AuthContext';
import { ErrorAlert } from '../components/ErrorAlert';
import { formatMoney, formatUtc, isMonth } from '../components/format';
import { MonthField } from '../components/MonthField';
import { PageHeader } from '../components/PageHeader';
import { PayrollRunStatusBadge } from '../components/StatusBadge';
import { useToast } from '../components/toastContext';
import { currentMonthUtc } from '../forms/dates';

export function PayrollPage() {
  const { hasRole } = useAuth();
  const { notify } = useToast();
  const navigate = useNavigate();
  const [month, setMonth] = useState(currentMonthUtc());
  const runs = usePayrollRuns(month);
  const periods = usePayrollPeriods();
  const generate = useGeneratePayrollRun();
  // A locked month is finished: offering "Generate" there only leads to a refusal, so show it as done instead.
  const lockedPeriod = periods.data?.find((p) => p.month === month && p.isLocked);

  return (
    <Stack spacing={3}>
      <PageHeader
        title="Payroll"
        subtitle="Finance generates and reviews a run, a different Manager approves it, then Finance finalizes and locks it."
        actions={
          <>
            <MonthField
              label="Month"
              value={month}
              onChange={(value) => {
                generate.reset();
                setMonth(value);
              }}
            />
            {hasRole('Finance') && !lockedPeriod && (
              <Button
                variant="contained"
                disabled={!isMonth(month) || generate.isPending}
                onClick={() => {
                  generate.mutate(month, {
                    onSuccess: (detail) => {
                      notify(`Generated ${detail.run.periodMonth} run v${detail.run.runVersion}.`);
                      void navigate(`/payroll/runs/${detail.run.id}`);
                    },
                  });
                }}
              >
                Generate run for {month || '…'}
              </Button>
            )}
          </>
        }
      />
      {lockedPeriod ? (
        <Alert severity="success">
          Payroll for {month} is complete and locked. Payslips are published; pick the next month to run payroll again.
        </Alert>
      ) : (
        <ErrorAlert error={generate.error ?? runs.error} />
      )}
      <section aria-labelledby="runs-heading">
        <Typography id="runs-heading" variant="h6" component="h2" gutterBottom>
          Runs {month && `for ${month}`}
        </Typography>
        {runs.isLoading && <CircularProgress aria-label="Loading runs" />}
        {runs.data && (
          <TableContainer component={Paper}>
            <Table size="small" aria-label="Payroll runs">
              <TableHead>
                <TableRow>
                  <TableCell>Run</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell align="right">Employees</TableCell>
                  <TableCell align="right">Applied</TableCell>
                  <TableCell align="right">Carried forward</TableCell>
                  <TableCell align="right">Refunds</TableCell>
                  <TableCell>Generated</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {runs.data.map((r) => (
                  <TableRow key={r.id} hover>
                    <TableCell>
                      <Link component={RouterLink} to={`/payroll/runs/${r.id}`}>
                        {r.periodMonth} v{r.runVersion}
                      </Link>
                    </TableCell>
                    <TableCell>
                      <PayrollRunStatusBadge status={r.status} />
                    </TableCell>
                    <TableCell align="right">{r.employeeCount}</TableCell>
                    <TableCell align="right">{formatMoney(r.totalApplied)}</TableCell>
                    <TableCell align="right">{formatMoney(r.totalCarriedForward)}</TableCell>
                    <TableCell align="right">{formatMoney(r.totalRefunds)}</TableCell>
                    <TableCell>{formatUtc(r.generatedAtUtc)}</TableCell>
                  </TableRow>
                ))}
                {runs.data.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={7}>No runs for this month yet.</TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </section>
      <section aria-labelledby="periods-heading">
        <Typography id="periods-heading" variant="h6" component="h2" gutterBottom>
          Periods
        </Typography>
        <ErrorAlert error={periods.error} />
        {periods.data && (
          <TableContainer component={Paper}>
            <Table size="small" aria-label="Payroll periods">
              <TableHead>
                <TableRow>
                  <TableCell>Month</TableCell>
                  <TableCell>State</TableCell>
                  <TableCell>Finalized run</TableCell>
                  <TableCell>Locked at</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {periods.data.map((p) => (
                  <TableRow key={p.month}>
                    <TableCell>
                      <Button
                        size="small"
                        onClick={() => {
                          generate.reset();
                          setMonth(p.month);
                        }}
                        aria-label={`Show runs for ${p.month}`}
                      >
                        {p.month}
                      </Button>
                    </TableCell>
                    <TableCell>{p.isLocked ? 'Locked' : 'Open'}</TableCell>
                    <TableCell>
                      {p.finalizedRunId ? (
                        <Link component={RouterLink} to={`/payroll/runs/${p.finalizedRunId}`}>
                          Run #{p.finalizedRunId}
                        </Link>
                      ) : (
                        '–'
                      )}
                    </TableCell>
                    <TableCell>{formatUtc(p.lockedAtUtc)}</TableCell>
                  </TableRow>
                ))}
                {periods.data.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={4}>No periods yet.</TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </section>
    </Stack>
  );
}
