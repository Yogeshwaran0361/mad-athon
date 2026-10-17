import Grid from '@mui/material/Grid2';
import { useState } from 'react';
import { Card, CardContent, CircularProgress, Stack, Typography, useTheme } from '@mui/material';
import { Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { useReport } from '../api/hooks';
import type { ByEmployeeRow, ByModuleRow, ReworkSummaryRow } from '../api/types';
import { ErrorAlert } from '../components/ErrorAlert';
import { formatMoney, formatPercent, isMonth } from '../components/format';
import { MonthField } from '../components/MonthField';
import { PageHeader } from '../components/PageHeader';
import { Stat } from '../components/Stat';
import { currentMonthUtc } from '../forms/dates';

function monthsAgo(n: number): string {
  const d = new Date();
  d.setUTCDate(1);
  d.setUTCMonth(d.getUTCMonth() - n);
  return d.toISOString().slice(0, 7);
}

function ChartCard({ title, summary, children }: { title: string; summary: string; children: React.ReactElement }) {
  return (
    <Card variant="outlined" component="figure" sx={{ m: 0, height: '100%' }}>
      <CardContent>
        <Typography variant="h6" component="figcaption">
          {title}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          {summary}
        </Typography>
        <div role="img" aria-label={`${title}. ${summary}`} style={{ width: '100%', height: 280 }}>
          <ResponsiveContainer width="100%" height="100%">
            {children}
          </ResponsiveContainer>
        </div>
      </CardContent>
    </Card>
  );
}

export function DashboardPage() {
  const theme = useTheme();
  const [from, setFrom] = useState(monthsAgo(5));
  const [to, setTo] = useState(currentMonthUtc());
  const valid = isMonth(from) && isMonth(to) && from <= to;
  const params = { from, to };
  const summary = useReport<ReworkSummaryRow>('rework-summary', params, valid);
  const byEmployee = useReport<ByEmployeeRow>('by-employee', params, valid);
  const byModule = useReport<ByModuleRow>('by-module', params, valid);

  const rows = summary.data?.rows ?? [];
  const totals = rows.reduce(
    (acc, r) => ({
      regular: acc.regular + r.regularHours,
      corrective: acc.corrective + r.correctiveHours,
      bugs: acc.bugs + r.bugsReported,
      applied: acc.applied + r.deductionsApplied,
    }),
    { regular: 0, corrective: 0, bugs: 0, applied: 0 },
  );
  const reworkPct = totals.regular + totals.corrective > 0 ? (totals.corrective / (totals.regular + totals.corrective)) * 100 : 0;
  const topModules = [...(byModule.data?.rows ?? [])].sort((a, b) => b.correctiveHours - a.correctiveHours).slice(0, 8);
  const topEmployees = [...(byEmployee.data?.rows ?? [])].sort((a, b) => b.correctiveHours - a.correctiveHours).slice(0, 8);
  const primary = theme.palette.primary.main;
  const secondary = theme.palette.warning.dark;

  return (
    <Stack spacing={3}>
      <PageHeader
        title="Dashboard"
        subtitle="Rework and deductions over time. Full tables and exports are on the Reports page."
        actions={
          <>
            <MonthField label="From" value={from} onChange={setFrom} />
            <MonthField label="To" value={to} onChange={setTo} />
          </>
        }
      />
      {!valid && <ErrorAlert error={new Error('Choose a valid month range (From must not be after To).')} />}
      <ErrorAlert error={summary.error ?? byEmployee.error ?? byModule.error} />
      {summary.isLoading && <CircularProgress aria-label="Loading dashboard" />}
      <Grid container spacing={2}>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Rework share" value={formatPercent(Math.round(reworkPct * 10) / 10)} hint="Corrective ÷ all hours" />
        </Grid>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Corrective hours" value={totals.corrective.toFixed(1)} />
        </Grid>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Bugs reported" value={String(totals.bugs)} />
        </Grid>
        <Grid size={{ xs: 6, md: 3 }}>
          <Stat label="Deductions applied" value={formatMoney(totals.applied)} />
        </Grid>
        <Grid size={{ xs: 12, md: 6 }}>
          <ChartCard title="Hours by month" summary={`Regular versus corrective hours, ${from} to ${to}.`}>
            <BarChart data={rows}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="month" />
              <YAxis />
              <Tooltip />
              <Legend />
              <Bar dataKey="regularHours" name="Regular hours" stackId="h" fill={primary} />
              <Bar dataKey="correctiveHours" name="Corrective hours" stackId="h" fill={secondary} />
            </BarChart>
          </ChartCard>
        </Grid>
        <Grid size={{ xs: 12, md: 6 }}>
          <ChartCard title="Rework percentage" summary="Share of hours spent on corrective work, per month.">
            <LineChart data={rows}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="month" />
              <YAxis unit="%" />
              <Tooltip />
              <Line type="monotone" dataKey="reworkPercent" name="Rework %" stroke={primary} strokeWidth={2} dot />
            </LineChart>
          </ChartCard>
        </Grid>
        <Grid size={{ xs: 12, md: 6 }}>
          <ChartCard title="Top modules by corrective hours" summary={`The ${topModules.length} modules with the most rework.`}>
            <BarChart data={topModules} layout="vertical" margin={{ left: 40 }}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis type="number" />
              <YAxis type="category" dataKey="moduleName" width={120} />
              <Tooltip />
              <Bar dataKey="correctiveHours" name="Corrective hours" fill={secondary} />
            </BarChart>
          </ChartCard>
        </Grid>
        <Grid size={{ xs: 12, md: 6 }}>
          <ChartCard title="Deductions by employee" summary={`Applied deductions for the ${topEmployees.length} employees with the most corrective hours.`}>
            <BarChart data={topEmployees} layout="vertical" margin={{ left: 40 }}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis type="number" />
              <YAxis type="category" dataKey="employeeName" width={120} />
              <Tooltip />
              <Legend />
              <Bar dataKey="deductionsApplied" name="Applied" fill={primary} />
              <Bar dataKey="carriedForward" name="Carried forward" fill={secondary} />
            </BarChart>
          </ChartCard>
        </Grid>
      </Grid>
    </Stack>
  );
}
