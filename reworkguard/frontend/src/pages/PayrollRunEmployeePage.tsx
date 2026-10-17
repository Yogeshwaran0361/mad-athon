import Grid from '@mui/material/Grid2';
import { Link as RouterLink, useParams } from 'react-router-dom';
import { CircularProgress, Link, Stack } from '@mui/material';
import { usePayrollRunEmployee } from '../api/hooks';
import { ErrorAlert } from '../components/ErrorAlert';
import { formatMoney } from '../components/format';
import { PageHeader } from '../components/PageHeader';
import { RunItemsTable } from '../components/RunItemsTable';
import { Stat } from '../components/Stat';

export function PayrollRunEmployeePage() {
  const params = useParams();
  const runId = Number(params.id);
  const employeeId = Number(params.employeeId);
  const { data, isLoading, error } = usePayrollRunEmployee(runId, employeeId);
  if (isLoading) {
    return <CircularProgress aria-label="Loading employee" />;
  }
  if (!data) {
    return <ErrorAlert error={error ?? new Error('Not found.')} />;
  }
  const e = data.employee;
  return (
    <Stack spacing={3}>
      <PageHeader
        title={`${e.employeeName} (${e.employeeCode})`}
        subtitle={
          <>
            <Link component={RouterLink} to={`/payroll/runs/${runId}`}>
              Back to run #{runId}
            </Link>{' '}
            · {e.payMode}, {e.salaryBasis}
            {e.manualSettlement && ' · has left: amounts are settled manually'}
          </>
        }
      />
      <Grid container spacing={2}>
        {(
          [
            ['Salary base', e.salaryBase],
            ['Cap', e.capAmount],
            ['Carried in', e.carriedIn],
            ['Current due', e.currentDue],
            ['Refunds', e.refunds],
            ['Applied', e.applied],
            ['Carried forward', e.carriedForward],
            ['Net adjustment', e.netAdjustment],
          ] as const
        ).map(([label, value]) => (
          <Grid key={label} size={{ xs: 6, md: 3 }}>
            <Stat label={label} value={formatMoney(value)} />
          </Grid>
        ))}
      </Grid>
      <RunItemsTable items={data.items} caption={`Lines for ${e.employeeName}`} />
    </Stack>
  );
}
