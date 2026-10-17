import { useState } from 'react';
import { Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, Stack, Typography } from '@mui/material';
import type { PayrollRunSummary, PayrollStep, Role } from '../api/types';

interface StepDef {
  step: PayrollStep;
  label: string;
  role: Role;
  from: PayrollRunSummary['status'];
  confirm?: string;
}

const STEPS: StepDef[] = [
  { step: 'review', label: 'Mark reviewed', role: 'Finance', from: 'Preview' },
  { step: 'approve', label: 'Approve', role: 'Manager', from: 'Reviewed' },
  {
    step: 'finalize',
    label: 'Finalize',
    role: 'Finance',
    from: 'Approved',
    confirm: 'Finalizing re-checks the inputs and reconciliation, locks the deduction lines and locks the month for new changes.',
  },
  { step: 'lock', label: 'Lock & apply', role: 'Finance', from: 'Finalized', confirm: 'Locking applies every line in this run. This cannot be undone.' },
];

/** The next workflow step for this run, if the user's role may take it. Steps send the run version the user is viewing. */
export function PayrollStepButtons({
  run,
  role,
  userId,
  busy,
  onStep,
}: {
  run: PayrollRunSummary;
  role: Role | undefined;
  userId: number | undefined;
  busy: boolean;
  onStep: (step: PayrollStep, expectedVersion: number) => void;
}) {
  const [confirming, setConfirming] = useState<StepDef | null>(null);
  const next = STEPS.find((s) => s.from === run.status);
  if (!next) {
    return (
      <Typography variant="body2" color="text.secondary">
        {run.status === 'Locked' ? 'This run is locked and applied.' : run.status === 'Superseded' ? 'This run was superseded by a newer version.' : null}
      </Typography>
    );
  }
  if (next.role !== role) {
    return (
      <Typography variant="body2" color="text.secondary">
        Waiting for {next.role === 'Manager' ? 'a Manager' : 'Finance'} to {next.label.toLowerCase()}.
      </Typography>
    );
  }
  const conflictOfInterest = next.step === 'approve' && userId !== undefined && (run.generatedByEmployeeId === userId || run.reviewedByEmployeeId === userId);
  return (
    <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
      <Button
        variant="contained"
        color={next.step === 'lock' ? 'warning' : 'primary'}
        disabled={busy || conflictOfInterest}
        onClick={() => {
          if (next.confirm) {
            setConfirming(next);
          } else {
            onStep(next.step, run.version);
          }
        }}
      >
        {next.label}
      </Button>
      {conflictOfInterest && (
        <Typography variant="body2" color="text.secondary">
          You generated or reviewed this run, so another Manager must approve it.
        </Typography>
      )}
      <Dialog
        open={confirming !== null}
        onClose={() => {
          setConfirming(null);
        }}
        aria-labelledby="step-confirm-title"
      >
        <DialogTitle id="step-confirm-title">
          {confirming?.label} run {run.periodMonth} v{run.runVersion}?
        </DialogTitle>
        <DialogContent>
          <DialogContentText>{confirming?.confirm}</DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button
            onClick={() => {
              setConfirming(null);
            }}
          >
            Cancel
          </Button>
          <Button
            variant="contained"
            autoFocus
            onClick={() => {
              if (confirming) {
                onStep(confirming.step, run.version);
              }
              setConfirming(null);
            }}
          >
            {confirming?.label}
          </Button>
        </DialogActions>
      </Dialog>
    </Stack>
  );
}
