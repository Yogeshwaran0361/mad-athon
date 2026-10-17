import Grid from '@mui/material/Grid2';
import { Link as RouterLink, useParams } from 'react-router-dom';
import { Card, CardContent, CircularProgress, Link, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { usePayrollRun, usePayrollStep } from '../api/hooks';
import type { PayrollRunDetail } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorAlert } from '../components/ErrorAlert';
import { formatMoney, formatPercent, formatUtc } from '../components/format';
import { PageHeader } from '../components/PageHeader';
import { PayrollStepButtons } from '../components/PayrollStepButtons';
import { ReconciliationList } from '../components/ReconciliationList';
import { Stat } from '../components/Stat';
import { PayrollRunStatusBadge } from '../components/StatusBadge';
import { useToast } from '../components/toastContext';

export function PayrollRunPage() {
  const id = Number(useParams().id);
  const { data, isLoading, error, refetch } = usePayrollRun(id);
  if (isLoading) {
    return <CircularProgress aria-label="Loading run" />;
  }
  if (!data) {
    return <ErrorAlert error={error ?? new Error('Run not found.')} />;
  }
  return (
    <RunView
      detail={data}
      onReload={() => {
        void refetch();
      }}
    />
  );
}

function RunView({ detail, onReload }: { detail: PayrollRunDetail; onReload: () => void }) {
  const { run } = detail;
  const { user } = useAuth();
  const { notify } = useToast();
  const step = usePayrollStep(run.id);

  const timeline: [string, string | null][] = [
    ['Generated', run.generatedAtUtc],
    ['Reviewed', run.reviewedAtUtc],
    ['Approved', run.approvedAtUtc],
    ['Finalized', run.finalizedAtUtc],
    ['Locked', run.lockedAtUtc],
    ['Superseded', run.supersededAtUtc],
  ];

  return (
    <Stack spacing={3}>
      <PageHeader
        title={`Payroll run ${run.periodMonth} v${run.runVersion}`}
        subtitle={
          <>
            <Link component={RouterLink} to="/payroll">
              All runs
            </Link>{' '}
            · cap {formatPercent(run.capPercent)} · inputs <code title={run.inputsHash}>{run.inputsHash.slice(0, 12)}</code> · version {run.version}
          </>
        }
        actions={<PayrollRunStatusBadge status={run.status} />}
      />
      <Card variant="outlined">
        <CardContent>
          <Stack spacing={1}>
            <PayrollStepButtons
              run={run}
              role={user?.role}
              userId={user?.id}
              busy={step.isPending}
              onStep={(s, expectedVersion) => {
                step.mutate(
                  { step: s, expectedVersion },
                  {
                    onSuccess: (d) => {
                      notify(`Run is now ${d.run.status}.`);
                    },
                  },
                );
              }}
            />
            <ErrorAlert
              error={step.error}
              onReload={() => {
                step.reset();
                onReload();
              }}
            />
            <Typography variant="body2" color="text.secondary">
              {timeline
                .filter(([, at]) => at)
                .map(([label, at]) => `${label} ${formatUtc(at)}`)
                .join(' · ')}
            </Typography>
          </Stack>
        </CardContent>
      </Card>
      <Grid container spacing={2}>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Employees" value={String(run.employeeCount)} />
        </Grid>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Applied" value={formatMoney(run.totalApplied)} />
        </Grid>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Carried forward" value={formatMoney(run.totalCarriedForward)} />
        </Grid>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Refunds" value={formatMoney(run.totalRefunds)} hint={`Settled manually: ${formatMoney(run.totalSettledManually)}`} />
        </Grid>
      </Grid>
      <section aria-labelledby="recon-heading">
        <Typography id="recon-heading" variant="h6" component="h2">
          Reconciliation
        </Typography>
        <ReconciliationList checks={detail.reconciliation} />
      </section>
      <section aria-labelledby="emp-heading">
        <Typography id="emp-heading" variant="h6" component="h2" gutterBottom>
          Employees
        </Typography>
        <TableContainer component={Paper}>
          <Table size="small" aria-label="Employees in this run">
            <TableHead>
              <TableRow>
                <TableCell>Employee</TableCell>
                <TableCell>Pay</TableCell>
                <TableCell align="right">Salary base</TableCell>
                <TableCell align="right">Cap</TableCell>
                <TableCell align="right">Carried in</TableCell>
                <TableCell align="right">Current due</TableCell>
                <TableCell align="right">Refunds</TableCell>
                <TableCell align="right">Applied</TableCell>
                <TableCell align="right">Carried forward</TableCell>
                <TableCell align="right">Net adjustment</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {detail.employees.map((e) => (
                <TableRow key={e.employeeId} hover>
                  <TableCell>
                    <Link component={RouterLink} to={`/payroll/runs/${run.id}/employees/${e.employeeId}`}>
                      {e.employeeName}
                    </Link>{' '}
                    <Typography variant="caption" color="text.secondary">
                      {e.employeeCode}
                      {e.manualSettlement && ' · manual settlement'}
                    </Typography>
                  </TableCell>
                  <TableCell>
                    {e.payMode} <Typography variant="caption">({e.salaryBasis})</Typography>
                  </TableCell>
                  <TableCell align="right">{formatMoney(e.salaryBase)}</TableCell>
                  <TableCell align="right">{formatMoney(e.capAmount)}</TableCell>
                  <TableCell align="right">{formatMoney(e.carriedIn)}</TableCell>
                  <TableCell align="right">{formatMoney(e.currentDue)}</TableCell>
                  <TableCell align="right">{formatMoney(e.refunds)}</TableCell>
                  <TableCell align="right">{formatMoney(e.applied)}</TableCell>
                  <TableCell align="right">{formatMoney(e.carriedForward)}</TableCell>
                  <TableCell align="right">{formatMoney(e.netAdjustment)}</TableCell>
                </TableRow>
              ))}
              {detail.employees.length === 0 && (
                <TableRow>
                  <TableCell colSpan={10}>No employees have deductions in this run.</TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      </section>
    </Stack>
  );
}
