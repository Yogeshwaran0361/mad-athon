import Grid from '@mui/material/Grid2';
import { useState } from 'react';
import { Alert, Button, CircularProgress, Link, Stack, TextField, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import { useDeductionPreview, useEmployees } from '../api/hooks';
import type { DeductionPreview } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorAlert } from '../components/ErrorAlert';
import { formatMoney, formatPercent } from '../components/format';
import { MonthField } from '../components/MonthField';
import { PageHeader } from '../components/PageHeader';
import { PayBreakdown } from '../components/PayBreakdown';
import { PreviewLinesTable } from '../components/PreviewLinesTable';
import { Stat } from '../components/Stat';

export function DeductionPreviewPage() {
  const { user, hasRole } = useAuth();
  const canPickEmployee = hasRole('TeamLead', 'Manager', 'Finance', 'Admin');
  const [employeeId, setEmployeeId] = useState<number | null>(user?.id ?? null);
  const [month, setMonth] = useState('');
  const employees = useEmployees(canPickEmployee);
  const { data, isFetching, error } = useDeductionPreview(employeeId, month);

  return (
    <Stack spacing={2}>
      <PageHeader title="Deduction preview" subtitle="What a pay month's payroll deducts. Leave the month empty for the first open month." />
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ alignItems: { sm: 'center' } }}>
        {canPickEmployee && (
          <TextField
            select
            size="small"
            label="Employee"
            value={employeeId ?? ''}
            onChange={(e) => {
              setEmployeeId(e.target.value === '' ? null : Number(e.target.value));
            }}
            slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
            sx={{ minWidth: 240 }}
          >
            {user && <option value={user.id}>Me ({user.name})</option>}
            {employees.data?.items
              .filter((e) => e.id !== user?.id)
              .map((e) => (
                <option key={e.id} value={e.id}>
                  {e.name} ({e.payMode})
                </option>
              ))}
          </TextField>
        )}
        <MonthField label="Pay month" value={month} onChange={setMonth} />
        {month && (
          <Button
            onClick={() => {
              setMonth('');
            }}
          >
            First open month
          </Button>
        )}
      </Stack>
      <ErrorAlert error={error} />
      {isFetching && <CircularProgress size={24} aria-label="Loading preview" />}
      {data && <PreviewView preview={data} />}
    </Stack>
  );
}

function PreviewView({ preview }: { preview: DeductionPreview }) {
  const finalized = preview.payrollRunId !== null;
  return (
    <Stack spacing={2}>
      <Alert severity={finalized ? 'success' : 'info'}>
        {preview.employeeName} ({preview.payMode}) · pay month {preview.periodMonth} · source: <strong>{preview.source}</strong>
        {preview.payrollRunId !== null && (
          <>
            {' '}
            from{' '}
            <Link component={RouterLink} to={`/payroll/runs/${preview.payrollRunId}`}>
              payroll run #{preview.payrollRunId}
            </Link>
          </>
        )}
        {!finalized && '. Estimate from approved lines; pending lines are not included in the figures.'}
      </Alert>
      <Grid container spacing={2}>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Salary base" value={formatMoney(preview.salaryBase)} hint={preview.salaryBasis} />
        </Grid>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label={`Cap (${formatPercent(preview.capPercent)})`} value={formatMoney(preview.capAmount)} />
        </Grid>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Carried in" value={formatMoney(preview.carriedIn)} />
        </Grid>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Current due" value={formatMoney(preview.currentDue)} />
        </Grid>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Refunds" value={formatMoney(preview.refunds)} />
        </Grid>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Applied this month" value={formatMoney(preview.applied)} />
        </Grid>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Carried forward" value={formatMoney(preview.carriedForward)} />
        </Grid>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Net adjustment" value={formatMoney(preview.netAdjustment)} hint={preview.settledManually ? `Settled manually: ${formatMoney(preview.settledManually)}` : undefined} />
        </Grid>
      </Grid>
      <PayBreakdown gross={preview.salaryBase} earningLines={preview.earningLines ?? []} applied={preview.applied} refunds={preview.refunds} net={preview.netPay} />
      <Typography variant="h6" component="h2">
        Lines
      </Typography>
      <PreviewLinesTable lines={preview.lines} caption="Deduction lines in the figures" emptyText="No deductions for this month." />
      <Typography variant="h6" component="h2">
        Pending (not yet approved)
      </Typography>
      <PreviewLinesTable lines={preview.pending} caption="Pending deduction lines" emptyText="Nothing pending." />
    </Stack>
  );
}
