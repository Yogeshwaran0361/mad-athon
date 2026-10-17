import Grid from '@mui/material/Grid2';
import { useEffect, useState } from 'react';
import {
  Button,
  Card,
  CardContent,
  Checkbox,
  CircularProgress,
  FormControlLabel,
  IconButton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import { useAddHoliday, useHolidays, usePolicy, useProjectWarranties, useRemoveHoliday, useUpdatePolicy, useUpdateWarranty } from '../../api/hooks';
import type { Policy, ProjectWarranty } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { ErrorAlert } from '../../components/ErrorAlert';
import { PageHeader } from '../../components/PageHeader';
import { useToast } from '../../components/toastContext';

const NUMBER_FIELDS: { key: keyof Policy; label: string; step: number }[] = [
  { key: 'standardHoursPerMonth', label: 'Standard hours per month', step: 0.01 },
  { key: 'dailyHours', label: 'Daily hours', step: 0.01 },
  { key: 'deductionCapPercent', label: 'Deduction cap (%)', step: 0.01 },
  { key: 'defaultWarrantyDays', label: 'Default warranty (days)', step: 1 },
  { key: 'disputeWindowDays', label: 'Dispute window (days)', step: 1 },
  { key: 'qaMissSharePercent', label: 'QA-miss share (%)', step: 0.01 },
];

function PolicyCard({ editable }: { editable: boolean }) {
  const { data, isLoading, error } = usePolicy();
  const update = useUpdatePolicy();
  const { notify } = useToast();
  const [draft, setDraft] = useState<Policy | null>(null);
  useEffect(() => {
    if (data) {
      setDraft(data);
    }
  }, [data]);

  return (
    <Card variant="outlined">
      <CardContent>
        <Typography variant="h6" component="h2" gutterBottom>
          Policy
        </Typography>
        <Typography variant="body2" color="text.secondary" gutterBottom>
          Changes apply to computations from now on; finalized payroll runs keep their snapshot.
        </Typography>
        <ErrorAlert error={error ?? update.error} />
        {isLoading && <CircularProgress aria-label="Loading policy" />}
        {draft && (
          <Stack
            component="form"
            spacing={2}
            onSubmit={(e) => {
              e.preventDefault();
              update.mutate(draft, {
                onSuccess: () => {
                  notify('Policy saved.');
                },
              });
            }}
          >
            {NUMBER_FIELDS.map((f) => (
              <TextField
                key={f.key}
                size="small"
                type="number"
                label={f.label}
                value={draft[f.key] as number}
                disabled={!editable}
                onChange={(e) => {
                  setDraft({ ...draft, [f.key]: Number(e.target.value) });
                }}
                slotProps={{ htmlInput: { step: f.step, min: 0 } }}
              />
            ))}
            <FormControlLabel
              control={
                <Checkbox
                  checked={draft.qaMissDeductible}
                  disabled={!editable}
                  onChange={(e) => {
                    setDraft({ ...draft, qaMissDeductible: e.target.checked });
                  }}
                />
              }
              label="QA misses are deductible"
            />
            <FormControlLabel
              control={
                <Checkbox
                  checked={draft.requireEmployeeAcknowledgement}
                  disabled={!editable}
                  onChange={(e) => {
                    setDraft({ ...draft, requireEmployeeAcknowledgement: e.target.checked });
                  }}
                />
              }
              label="Require employee acknowledgement"
            />
            {editable && (
              <Button type="submit" variant="contained" disabled={update.isPending}>
                Save policy
              </Button>
            )}
          </Stack>
        )}
      </CardContent>
    </Card>
  );
}

function WarrantyRow({ project, editable }: { project: ProjectWarranty; editable: boolean }) {
  const update = useUpdateWarranty();
  const { notify } = useToast();
  const [days, setDays] = useState(String(project.warrantyDays));
  return (
    <TableRow>
      <TableCell>{project.projectName}</TableCell>
      <TableCell>
        {editable ? (
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
            <TextField
              size="small"
              type="number"
              value={days}
              onChange={(e) => {
                setDays(e.target.value);
              }}
              slotProps={{ htmlInput: { min: 0, max: 3650, 'aria-label': `Warranty days for ${project.projectName}` } }}
              sx={{ width: 110 }}
            />
            <Button
              size="small"
              disabled={update.isPending || Number(days) === project.warrantyDays}
              onClick={() => {
                update.mutate(
                  { id: project.projectId, warrantyDays: Number(days) },
                  {
                    onSuccess: () => {
                      notify(`Warranty for ${project.projectName} set to ${days} days.`);
                    },
                  },
                );
              }}
            >
              Save
            </Button>
          </Stack>
        ) : (
          `${project.warrantyDays} days`
        )}
        <ErrorAlert error={update.error} />
      </TableCell>
    </TableRow>
  );
}

function WarrantyCard({ editable }: { editable: boolean }) {
  const { data, error } = useProjectWarranties();
  return (
    <Card variant="outlined">
      <CardContent>
        <Typography variant="h6" component="h2" gutterBottom>
          Project warranty windows
        </Typography>
        <ErrorAlert error={error} />
        <Table size="small" aria-label="Project warranty windows">
          <TableHead>
            <TableRow>
              <TableCell>Project</TableCell>
              <TableCell>Warranty</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {data?.map((p) => <WarrantyRow key={p.projectId} project={p} editable={editable} />)}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

function HolidaysCard({ editable }: { editable: boolean }) {
  const [year, setYear] = useState(new Date().getUTCFullYear());
  const { data, error } = useHolidays(year);
  const add = useAddHoliday();
  const remove = useRemoveHoliday();
  const { notify } = useToast();
  const [date, setDate] = useState('');
  const [name, setName] = useState('');
  return (
    <Card variant="outlined">
      <CardContent>
        <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center' }}>
          <Typography variant="h6" component="h2">
            Holidays
          </Typography>
          <TextField
            size="small"
            type="number"
            label="Year"
            value={year}
            onChange={(e) => {
              setYear(Number(e.target.value));
            }}
            sx={{ width: 110 }}
          />
        </Stack>
        <ErrorAlert error={error ?? add.error ?? remove.error} />
        <Table size="small" aria-label={`Holidays in ${year}`}>
          <TableHead>
            <TableRow>
              <TableCell>Date</TableCell>
              <TableCell>Name</TableCell>
              {editable && <TableCell align="right">Actions</TableCell>}
            </TableRow>
          </TableHead>
          <TableBody>
            {data?.map((h) => (
              <TableRow key={h.id}>
                <TableCell>{h.date}</TableCell>
                <TableCell>{h.name}</TableCell>
                {editable && (
                  <TableCell align="right">
                    <IconButton
                      size="small"
                      aria-label={`Remove holiday ${h.name} on ${h.date}`}
                      disabled={remove.isPending}
                      onClick={() => {
                        remove.mutate(h.id, {
                          onSuccess: () => {
                            notify(`Removed ${h.name}.`);
                          },
                        });
                      }}
                    >
                      <span aria-hidden="true">✕</span>
                    </IconButton>
                  </TableCell>
                )}
              </TableRow>
            ))}
            {data?.length === 0 && (
              <TableRow>
                <TableCell colSpan={3}>No holidays.</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
        {editable && (
          <Stack
            component="form"
            direction={{ xs: 'column', sm: 'row' }}
            spacing={1}
            sx={{ mt: 2 }}
            onSubmit={(e) => {
              e.preventDefault();
              add.mutate(
                { date, name: name.trim() },
                {
                  onSuccess: () => {
                    notify(`Added ${name.trim()}.`);
                    setDate('');
                    setName('');
                  },
                },
              );
            }}
          >
            <TextField
              size="small"
              type="date"
              label="Date"
              required
              value={date}
              onChange={(e) => {
                setDate(e.target.value);
              }}
              slotProps={{ inputLabel: { shrink: true } }}
            />
            <TextField
              size="small"
              label="Name"
              required
              value={name}
              onChange={(e) => {
                setName(e.target.value);
              }}
            />
            <Button type="submit" variant="outlined" disabled={add.isPending || !date || !name.trim()}>
              Add holiday
            </Button>
          </Stack>
        )}
      </CardContent>
    </Card>
  );
}

export function SettingsPage() {
  const { hasRole } = useAuth();
  const editable = hasRole('Admin');
  return (
    <Stack spacing={2}>
      <PageHeader title="Settings" subtitle={editable ? 'Every change is audited.' : 'Read-only: only administrators can change settings.'} />
      <Grid container spacing={2}>
        <Grid size={{ xs: 12, md: 6 }}>
          <PolicyCard editable={editable} />
        </Grid>
        <Grid size={{ xs: 12, md: 6 }}>
          <Stack spacing={2}>
            <WarrantyCard editable={editable} />
            <HolidaysCard editable={editable} />
          </Stack>
        </Grid>
      </Grid>
    </Stack>
  );
}
