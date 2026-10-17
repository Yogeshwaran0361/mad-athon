import { useState } from 'react';
import {
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
} from '@mui/material';
import { downloadFile } from '../api/client';
import { useEmployees, usePayslip, usePayslips } from '../api/hooks';
import type { PayslipSummary } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorAlert } from '../components/ErrorAlert';
import { formatMoney, formatUtc } from '../components/format';
import { MonthField } from '../components/MonthField';
import { PageHeader } from '../components/PageHeader';
import { PayBreakdown } from '../components/PayBreakdown';
import { RunItemsTable } from '../components/RunItemsTable';
import { useToast } from '../components/toastContext';

function PayslipDialog({ id, onClose, onDownload }: { id: number; onClose: () => void; onDownload: (p: PayslipSummary) => void }) {
  const { data, isLoading, error } = usePayslip(id);
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="lg" aria-labelledby="payslip-title">
      <DialogTitle id="payslip-title">{data ? `Payslip ${data.payslip.periodMonth} – ${data.payslip.employeeName}` : 'Payslip'}</DialogTitle>
      <DialogContent>
        <ErrorAlert error={error} />
        {isLoading && <CircularProgress aria-label="Loading payslip" />}
        {data && (
          <Stack spacing={2}>
            <div>
              Salary base {formatMoney(data.payslip.salaryBase)} · deducted {formatMoney(data.payslip.applied)} · refunds {formatMoney(data.payslip.refunds)} · net
              adjustment {formatMoney(data.payslip.netAdjustment)}
            </div>
            <PayBreakdown gross={data.payslip.salaryBase} earningLines={data.earningLines ?? []} applied={data.payslip.applied} refunds={data.payslip.refunds} net={data.payslip.netPay} />
            <RunItemsTable items={data.items} caption="Payslip lines" />
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        {data && (
          <Button
            onClick={() => {
              onDownload(data.payslip);
            }}
          >
            Download PDF
          </Button>
        )}
        <Button onClick={onClose}>Close</Button>
      </DialogActions>
    </Dialog>
  );
}

export function PayslipsPage() {
  const { user, hasRole } = useAuth();
  const { notify } = useToast();
  const seesAll = hasRole('Finance', 'Manager', 'Admin');
  const employees = useEmployees(seesAll);
  const [employeeId, setEmployeeId] = useState<number | null>(seesAll ? null : (user?.id ?? null));
  const [month, setMonth] = useState('');
  const { data, isLoading, error } = usePayslips({ employeeId, periodMonth: month });
  const [open, setOpen] = useState<number | null>(null);
  const [downloadError, setDownloadError] = useState<unknown>(null);

  const download = (p: PayslipSummary) => {
    setDownloadError(null);
    downloadFile(`/payslips/${p.id}/pdf`, `payslip-${p.periodMonth}-${p.employeeId}.pdf`).then(
      () => {
        notify(`Downloaded payslip for ${p.periodMonth}.`);
      },
      (e: unknown) => {
        setDownloadError(e);
      },
    );
  };

  return (
    <Stack spacing={2}>
      <PageHeader title="Payslips" subtitle={seesAll ? 'Published payslips for all employees.' : 'Your published payslips.'} />
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
        {seesAll && (
          <TextField
            select
            size="small"
            label="Employee"
            value={employeeId ?? ''}
            onChange={(e) => {
              setEmployeeId(e.target.value === '' ? null : Number(e.target.value));
            }}
            slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
            sx={{ minWidth: 220 }}
          >
            <option value="">All employees</option>
            {employees.data?.items.map((e) => (
              <option key={e.id} value={e.id}>
                {e.name}
              </option>
            ))}
          </TextField>
        )}
        <MonthField label="Month" value={month} onChange={setMonth} />
      </Stack>
      <ErrorAlert error={error ?? downloadError} />
      {isLoading && <CircularProgress aria-label="Loading payslips" />}
      {data && (
        <TableContainer component={Paper}>
          <Table size="small" aria-label="Payslips">
            <TableHead>
              <TableRow>
                <TableCell>Month</TableCell>
                <TableCell>Employee</TableCell>
                <TableCell align="right">Salary base</TableCell>
                <TableCell align="right">Deducted</TableCell>
                <TableCell align="right">Refunds</TableCell>
                <TableCell align="right">Net adjustment</TableCell>
                <TableCell>Published</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.map((p) => (
                <TableRow key={p.id}>
                  <TableCell>{p.periodMonth}</TableCell>
                  <TableCell>{p.employeeName}</TableCell>
                  <TableCell align="right">{formatMoney(p.salaryBase)}</TableCell>
                  <TableCell align="right">{formatMoney(p.applied)}</TableCell>
                  <TableCell align="right">{formatMoney(p.refunds)}</TableCell>
                  <TableCell align="right">{formatMoney(p.netAdjustment)}</TableCell>
                  <TableCell>{formatUtc(p.publishedAtUtc)}</TableCell>
                  <TableCell align="right">
                    <Button
                      size="small"
                      aria-label={`View payslip ${p.periodMonth} for ${p.employeeName}`}
                      onClick={() => {
                        setOpen(p.id);
                      }}
                    >
                      View
                    </Button>
                    <Button
                      size="small"
                      aria-label={`Download PDF payslip ${p.periodMonth} for ${p.employeeName}`}
                      onClick={() => {
                        download(p);
                      }}
                    >
                      PDF
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
              {data.length === 0 && (
                <TableRow>
                  <TableCell colSpan={8}>No payslips published.</TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      )}
      {open !== null && (
        <PayslipDialog
          id={open}
          onClose={() => {
            setOpen(null);
          }}
          onDownload={download}
        />
      )}
    </Stack>
  );
}
