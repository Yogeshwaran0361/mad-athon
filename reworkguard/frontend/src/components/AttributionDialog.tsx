import { useState } from 'react';
import {
  Alert,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import { useAttribution, useOverrideAttribution } from '../api/hooks';
import type { Attribution } from '../api/types';
import { ErrorAlert } from './ErrorAlert';
import { formatHours, formatPercent, formatUtc, humanize } from './format';
import { useToast } from './toastContext';

function OverrideForm({ attribution, onDone }: { attribution: Attribution; onDone: () => void }) {
  const override = useOverrideAttribution(attribution.bugId);
  const { notify } = useToast();
  const [shares, setShares] = useState<Record<number, string>>(() =>
    Object.fromEntries(attribution.shares.map((s) => [s.employeeId, String(s.sharePercent)])),
  );
  const [justification, setJustification] = useState('');
  const total = Object.values(shares).reduce((sum, v) => sum + (Number(v) || 0), 0);
  const valid = Math.abs(total - 100) < 0.005 && justification.trim().length >= 10;

  return (
    <Stack
      component="form"
      spacing={2}
      onSubmit={(e) => {
        e.preventDefault();
        if (!valid) {
          return;
        }
        override.mutate(
          {
            shares: Object.entries(shares)
              .map(([id, pct]) => ({ employeeId: Number(id), sharePercent: Number(pct) }))
              .filter((s) => s.sharePercent > 0),
            justification: justification.trim(),
          },
          {
            onSuccess: () => {
              notify('Attribution overridden.');
              onDone();
            },
          },
        );
      }}
    >
      <Typography variant="subtitle1" component="h3">
        Override shares
      </Typography>
      {attribution.shares.map((s) => (
        <TextField
          key={s.employeeId}
          size="small"
          type="number"
          label={`${s.employeeName} share (%)`}
          value={shares[s.employeeId] ?? ''}
          onChange={(e) => {
            setShares({ ...shares, [s.employeeId]: e.target.value });
          }}
          slotProps={{ htmlInput: { min: 0, max: 100, step: 0.01 } }}
        />
      ))}
      <Typography variant="body2" color={Math.abs(total - 100) < 0.005 ? 'text.secondary' : 'error'} aria-live="polite">
        Total: {formatPercent(total)} {Math.abs(total - 100) < 0.005 ? '(OK)' : '(must equal 100%)'}
      </Typography>
      <TextField
        label="Justification"
        multiline
        minRows={2}
        required
        value={justification}
        onChange={(e) => {
          setJustification(e.target.value);
        }}
        helperText="At least 10 characters."
      />
      <ErrorAlert error={override.error} />
      <Button type="submit" variant="contained" disabled={!valid || override.isPending}>
        Save override
      </Button>
    </Stack>
  );
}

/** Shows who a bug's deduction is attributed to and, for Managers, lets them override the split. */
export function AttributionDialog({ bugId, canOverride, onClose }: { bugId: number; canOverride: boolean; onClose: () => void }) {
  const { data, isLoading, error } = useAttribution(bugId);
  const [editing, setEditing] = useState(false);
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="attribution-title">
      <DialogTitle id="attribution-title">Attribution for bug #{bugId}</DialogTitle>
      <DialogContent>
        <ErrorAlert error={error} />
        {isLoading && <CircularProgress aria-label="Loading attribution" />}
        {data && (
          <Stack spacing={2}>
            <Alert severity={data.isDeductible ? 'warning' : 'info'}>
              {data.isDeductible ? 'Deductible.' : 'Not deductible.'} Source: {humanize(data.source)} · basis: {humanize(data.basis)}
              {data.regressionOfBugId !== null && ` · regression of bug #${data.regressionOfBugId}`}
            </Alert>
            <Table size="small" aria-label="Attribution shares">
              <TableHead>
                <TableRow>
                  <TableCell>Employee</TableCell>
                  <TableCell align="right">Basis hours</TableCell>
                  <TableCell align="right">Share</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {data.shares.map((s) => (
                  <TableRow key={s.employeeId}>
                    <TableCell>{s.employeeName}</TableCell>
                    <TableCell align="right">{formatHours(s.basisHours)}</TableCell>
                    <TableCell align="right">{formatPercent(s.sharePercent)}</TableCell>
                  </TableRow>
                ))}
                {data.shares.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={3}>No one is attributed.</TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
            {data.overrideJustification && (
              <Typography variant="body2">
                Overridden {formatUtc(data.overriddenAtUtc)}: {data.overrideJustification}
              </Typography>
            )}
            {canOverride && editing && (
              <OverrideForm
                attribution={data}
                onDone={() => {
                  setEditing(false);
                }}
              />
            )}
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        {canOverride && data && !editing && data.shares.length > 0 && (
          <Button
            onClick={() => {
              setEditing(true);
            }}
          >
            Override…
          </Button>
        )}
        <Button onClick={onClose}>Close</Button>
      </DialogActions>
    </Dialog>
  );
}
