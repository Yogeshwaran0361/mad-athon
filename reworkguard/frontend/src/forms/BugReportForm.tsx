import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Alert, Button, Stack, TextField } from '@mui/material';
import { BUG_SEVERITIES, type ReportBugInput } from '../api/types';
import { applyServerErrors } from './applyServerErrors';
import { todayUtc } from './dates';
import { bugReportSchema, type BugReportValues } from './schemas';

interface Props {
  onSubmit: (input: ReportBugInput) => Promise<void>;
  /** The task's completion date (yyyy-MM-dd): the earliest found date allowed. */
  completedOn?: string | null;
}

const fields = ['severity', 'description', 'foundDate'] as const;

export function BugReportForm({ onSubmit, completedOn }: Props) {
  const [formError, setFormError] = useState('');
  const defaultDate = () => (completedOn && completedOn > todayUtc() ? completedOn : todayUtc());
  const { control, handleSubmit, setError, reset, formState } = useForm<BugReportValues>({
    resolver: zodResolver(bugReportSchema(completedOn)),
    defaultValues: { severity: 'Medium', description: '', foundDate: defaultDate() },
  });

  const submit = handleSubmit(async (values) => {
    setFormError('');
    try {
      await onSubmit({ severity: values.severity as ReportBugInput['severity'], description: values.description, foundDate: values.foundDate });
      reset({ severity: 'Medium', description: '', foundDate: defaultDate() });
    } catch (error) {
      setFormError(applyServerErrors(error, fields, setError));
    }
  });

  return (
    <Stack component="form" spacing={2} noValidate onSubmit={(e) => void submit(e)} aria-label="Report a bug">
      {formError && <Alert severity="warning">{formError}</Alert>}
      <Controller
        name="severity"
        control={control}
        render={({ field, fieldState }) => (
          <TextField
            {...field}
            select
            label="Severity"
            error={!!fieldState.error}
            helperText={fieldState.error?.message}
            slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
          >
            {BUG_SEVERITIES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </TextField>
        )}
      />
      <Controller
        name="description"
        control={control}
        render={({ field, fieldState }) => (
          <TextField {...field} label="What is wrong?" multiline minRows={3} error={!!fieldState.error} helperText={fieldState.error?.message} />
        )}
      />
      <Controller
        name="foundDate"
        control={control}
        render={({ field, fieldState }) => (
          <TextField
            {...field}
            type="date"
            label="Found on"
            error={!!fieldState.error}
            helperText={fieldState.error?.message ?? (completedOn ? `Any date from ${completedOn} (task completed)` : undefined)}
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: completedOn ?? undefined } }}
          />
        )}
      />
      <Button type="submit" variant="contained" color="error" disabled={formState.isSubmitting}>
        Report bug and reopen task
      </Button>
    </Stack>
  );
}
