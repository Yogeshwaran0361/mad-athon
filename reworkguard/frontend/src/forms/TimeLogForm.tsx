import { useState } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Alert, Button, Stack, TextField } from '@mui/material';
import type { LogTimeInput, WorkType } from '../api/types';
import { applyServerErrors } from './applyServerErrors';
import { todayUtc } from './dates';
import { timeLogSchema, type TimeLogValues } from './schemas';

export interface OpenBugOption {
  id: number;
  description: string;
}

interface Props {
  taskId: number;
  /** Work types the task's current status accepts. */
  allowedTypes: WorkType[];
  openBugs: OpenBugOption[];
  onSubmit: (input: LogTimeInput) => Promise<void>;
}

const fields = ['workType', 'bugId', 'workDate', 'hours', 'note'] as const;

export function TimeLogForm({ taskId, allowedTypes, openBugs, onSubmit }: Props) {
  const [formError, setFormError] = useState('');
  const defaultType: WorkType = allowedTypes.includes('Corrective') ? 'Corrective' : (allowedTypes[0] ?? 'Regular');
  const defaults: TimeLogValues = {
    workType: defaultType,
    bugId: defaultType === 'Corrective' && openBugs.length === 1 && openBugs[0] ? String(openBugs[0].id) : '',
    workDate: todayUtc(),
    hours: '',
    note: '',
  };
  const { control, handleSubmit, setError, reset, formState } = useForm<TimeLogValues>({
    resolver: zodResolver(timeLogSchema),
    defaultValues: defaults,
  });
  const workType = useWatch({ control, name: 'workType' });

  const submit = handleSubmit(async (values) => {
    setFormError('');
    try {
      await onSubmit({
        taskId,
        workType: values.workType,
        bugId: values.workType === 'Corrective' ? Number(values.bugId) : null,
        workDate: values.workDate,
        hours: Number(values.hours),
        note: values.note.trim() === '' ? null : values.note.trim(),
      });
      reset(defaults);
    } catch (error) {
      setFormError(applyServerErrors(error, fields, setError));
    }
  });

  return (
    <Stack component="form" spacing={2} noValidate onSubmit={(e) => void submit(e)} aria-label="Log time">
      {formError && <Alert severity="warning">{formError}</Alert>}
      <Controller
        name="workType"
        control={control}
        render={({ field }) => (
          <TextField {...field} select label="Work type" slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}>
            {allowedTypes.map((t) => (
              <option key={t} value={t}>
                {t === 'Corrective' ? 'Corrective (fixing a bug)' : t === 'Enhancement' ? 'Enhancement (paid feature request)' : 'Regular'}
              </option>
            ))}
          </TextField>
        )}
      />
      {workType === 'Corrective' && (
        <Controller
          name="bugId"
          control={control}
          render={({ field, fieldState }) => (
            <TextField
              {...field}
              select
              label="Bug being fixed"
              error={!!fieldState.error}
              helperText={fieldState.error?.message}
              slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
            >
              <option value="">Choose a bug</option>
              {openBugs.map((b) => (
                <option key={b.id} value={String(b.id)}>
                  #{b.id} {b.description}
                </option>
              ))}
            </TextField>
          )}
        />
      )}
      <Controller
        name="workDate"
        control={control}
        render={({ field, fieldState }) => (
          <TextField
            {...field}
            type="date"
            label="Work date"
            error={!!fieldState.error}
            helperText={fieldState.error?.message}
            slotProps={{ inputLabel: { shrink: true } }}
          />
        )}
      />
      <Controller
        name="hours"
        control={control}
        render={({ field, fieldState }) => (
          <TextField
            {...field}
            label="Hours"
            inputMode="decimal"
            error={!!fieldState.error}
            helperText={fieldState.error?.message}
          />
        )}
      />
      <Controller
        name="note"
        control={control}
        render={({ field, fieldState }) => (
          <TextField {...field} label="Note (optional)" error={!!fieldState.error} helperText={fieldState.error?.message} />
        )}
      />
      <Button type="submit" variant="contained" disabled={formState.isSubmitting}>
        Log time
      </Button>
    </Stack>
  );
}
