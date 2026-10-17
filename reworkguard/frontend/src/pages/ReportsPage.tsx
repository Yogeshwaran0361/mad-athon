import { useState } from 'react';
import { CircularProgress, Paper, Stack, Tab, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Tabs } from '@mui/material';
import { useReport, type ReportName } from '../api/hooks';
import type { ByEmployeeRow, ByModuleRow, PayrollRegisterRow, ReworkSummaryRow } from '../api/types';
import { ErrorAlert } from '../components/ErrorAlert';
import { ExportButtons } from '../components/ExportButtons';
import { formatHours, formatMoney, formatPercent, isMonth } from '../components/format';
import { MonthField } from '../components/MonthField';
import { PageHeader } from '../components/PageHeader';
import { currentMonthUtc } from '../forms/dates';

interface Column {
  key: string;
  label: string;
  format?: (value: unknown) => string;
  numeric?: boolean;
}

const money = (v: unknown) => formatMoney(v as number);
const hours = (v: unknown) => formatHours(v as number);

const REPORTS: { name: ReportName; label: string; columns: Column[] }[] = [
  {
    name: 'rework-summary',
    label: 'Rework summary',
    columns: [
      { key: 'month' satisfies keyof ReworkSummaryRow, label: 'Month' },
      { key: 'regularHours', label: 'Regular hours', format: hours, numeric: true },
      { key: 'correctiveHours', label: 'Corrective hours', format: hours, numeric: true },
      { key: 'reworkPercent', label: 'Rework %', format: (v: unknown) => formatPercent(v as number), numeric: true },
      { key: 'bugsReported', label: 'Bugs reported', numeric: true },
      { key: 'bugsDeductible', label: 'Deductible bugs', numeric: true },
      { key: 'deductionsApplied', label: 'Deductions applied', format: money, numeric: true },
      { key: 'refunds', label: 'Refunds', format: money, numeric: true },
    ],
  },
  {
    name: 'by-employee',
    label: 'By employee',
    columns: [
      { key: 'employeeName' satisfies keyof ByEmployeeRow, label: 'Employee' },
      { key: 'correctiveHours', label: 'Corrective hours', format: hours, numeric: true },
      { key: 'deductionsApplied', label: 'Deductions applied', format: money, numeric: true },
      { key: 'carriedForward', label: 'Carried forward', format: money, numeric: true },
      { key: 'disputesRaised', label: 'Disputes raised', numeric: true },
      { key: 'disputesUpheld', label: 'Disputes upheld', numeric: true },
    ],
  },
  {
    name: 'by-module',
    label: 'By module',
    columns: [
      { key: 'projectName' satisfies keyof ByModuleRow, label: 'Project' },
      { key: 'moduleName', label: 'Module' },
      { key: 'bugs', label: 'Bugs', numeric: true },
      { key: 'reopens', label: 'Reopens', numeric: true },
      { key: 'correctiveHours', label: 'Corrective hours', format: hours, numeric: true },
      { key: 'deductionAmount', label: 'Deductions', format: money, numeric: true },
    ],
  },
  {
    name: 'payroll-register',
    label: 'Payroll register',
    columns: [
      { key: 'employeeCode' satisfies keyof PayrollRegisterRow, label: 'Code' },
      { key: 'employeeName', label: 'Employee' },
      { key: 'payMode', label: 'Pay mode' },
      { key: 'salaryBase', label: 'Salary base', format: money, numeric: true },
      { key: 'capAmount', label: 'Cap', format: money, numeric: true },
      { key: 'carriedIn', label: 'Carried in', format: money, numeric: true },
      { key: 'currentDue', label: 'Current due', format: money, numeric: true },
      { key: 'refunds', label: 'Refunds', format: money, numeric: true },
      { key: 'applied', label: 'Applied', format: money, numeric: true },
      { key: 'carriedForward', label: 'Carried forward', format: money, numeric: true },
      { key: 'settledManually', label: 'Settled manually', format: money, numeric: true },
      { key: 'netAdjustment', label: 'Net adjustment', format: money, numeric: true },
    ],
  },
];

export function ReportsPage() {
  const [tab, setTab] = useState(0);
  const [from, setFrom] = useState(currentMonthUtc());
  const [to, setTo] = useState(currentMonthUtc());
  const [period, setPeriod] = useState(currentMonthUtc());
  const report = REPORTS[tab] ?? REPORTS[0];
  const isRegister = report?.name === 'payroll-register';
  const params: Record<string, string> = isRegister ? { periodMonth: period } : { from, to };
  const valid = isRegister ? isMonth(period) : isMonth(from) && isMonth(to) && from <= to;
  const { data, isLoading, error } = useReport<Record<string, unknown>>(report?.name ?? 'rework-summary', params, valid);

  if (!report) {
    return null;
  }

  return (
    <Stack spacing={2}>
      <PageHeader title="Reports" subtitle="Pick a report and range, then export as Excel, CSV or PDF." />
      <Tabs
        value={tab}
        onChange={(_, v: number) => {
          setTab(v);
        }}
        aria-label="Reports"
        variant="scrollable"
      >
        {REPORTS.map((r) => (
          <Tab key={r.name} label={r.label} />
        ))}
      </Tabs>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ alignItems: { sm: 'center' } }}>
        {isRegister ? (
          <MonthField label="Pay month" value={period} onChange={setPeriod} required />
        ) : (
          <>
            <MonthField label="From" value={from} onChange={setFrom} required />
            <MonthField label="To" value={to} onChange={setTo} required />
          </>
        )}
        <ExportButtons report={report.name} params={params} label={report.label} disabled={!valid} />
      </Stack>
      {!valid && <ErrorAlert error={new Error('Choose a valid month range.')} />}
      <ErrorAlert error={error} />
      {isLoading && <CircularProgress aria-label="Loading report" />}
      {data && (
        <TableContainer component={Paper}>
          <Table size="small" aria-label={report.label}>
            <TableHead>
              <TableRow>
                {report.columns.map((c) => (
                  <TableCell key={c.key} align={c.numeric ? 'right' : 'left'}>
                    {c.label}
                  </TableCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {data.rows.map((row, i) => (
                // Report rows have no stable id across reports; position is stable for a given response.
                <TableRow key={i}>
                  {report.columns.map((c) => {
                    const value = row[c.key];
                    return (
                      <TableCell key={c.key} align={c.numeric ? 'right' : 'left'}>
                        {c.format ? c.format(value) : typeof value === 'string' || typeof value === 'number' ? String(value) : '–'}
                      </TableCell>
                    );
                  })}
                </TableRow>
              ))}
              {data.rows.length === 0 && (
                <TableRow>
                  <TableCell colSpan={report.columns.length}>No data for this range.</TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      )}
    </Stack>
  );
}
