import { useState } from 'react';
import {
  Button,
  CircularProgress,
  FormControlLabel,
  Radio,
  RadioGroup,
  Stack,
  Tab,
  Tabs,
  TextField,
  Typography,
} from '@mui/material';
import { useApproveDeductions, useCancelDeduction, useDeductions, useNotifyDeductions, useResolveDispute } from '../api/hooks';
import type { Deduction, DisputeOutcome } from '../api/types';
import { DeductionTable } from '../components/DeductionTable';
import { ErrorAlert } from '../components/ErrorAlert';
import { PageHeader } from '../components/PageHeader';
import { ReasonDialog } from '../components/ReasonDialog';
import { useToast } from '../components/toastContext';

function useSelection() {
  const [selected, setSelected] = useState<Set<number>>(new Set());
  return {
    selected,
    toggle: (id: number) => {
      setSelected((prev) => {
        const next = new Set(prev);
        if (next.has(id)) {
          next.delete(id);
        } else {
          next.add(id);
        }
        return next;
      });
    },
    setAll: (ids: number[], checked: boolean) => {
      setSelected(checked ? new Set(ids) : new Set());
    },
    clear: () => {
      setSelected(new Set());
    },
  };
}

function CancelButton({ row, onCancel }: { row: Deduction; onCancel: (row: Deduction) => void }) {
  return (
    <Button
      size="small"
      color="error"
      aria-label={`Cancel line ${row.id}`}
      onClick={() => {
        onCancel(row);
      }}
    >
      Cancel line
    </Button>
  );
}

function BatchQueue({
  rows,
  caption,
  actionLabel,
  emptyText,
  busy,
  error,
  onAction,
  onCancel,
}: {
  rows: Deduction[];
  caption: string;
  actionLabel: string;
  emptyText: string;
  busy: boolean;
  error: unknown;
  onAction: (ids: number[], done: () => void) => void;
  onCancel: (row: Deduction) => void;
}) {
  const selection = useSelection();
  const ids = [...selection.selected].filter((id) => rows.some((r) => r.id === id));
  return (
    <Stack spacing={1}>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
        <Button
          variant="contained"
          disabled={ids.length === 0 || busy}
          onClick={() => {
            onAction(ids, selection.clear);
          }}
        >
          {actionLabel} ({ids.length})
        </Button>
        <Typography variant="body2" color="text.secondary">
          All selected lines succeed or none do.
        </Typography>
      </Stack>
      <ErrorAlert error={error} />
      <DeductionTable
        rows={rows}
        caption={caption}
        selected={selection.selected}
        onToggle={selection.toggle}
        onToggleAll={(checked) => {
          selection.setAll(
            rows.map((r) => r.id),
            checked,
          );
        }}
        actions={(row) => <CancelButton row={row} onCancel={onCancel} />}
        emptyText={emptyText}
      />
    </Stack>
  );
}

function ResolveDialog({ row, onClose }: { row: Deduction; onClose: () => void }) {
  const resolve = useResolveDispute();
  const { notify } = useToast();
  const [outcome, setOutcome] = useState<DisputeOutcome>('Rejected');
  const [newHours, setNewHours] = useState('');
  const [newShare, setNewShare] = useState('');
  return (
    <ReasonDialog
      open
      title={`Resolve dispute on line #${row.id}`}
      description={`${row.employeeName}: "${row.dispute?.reason ?? ''}"`}
      label="Justification"
      minLength={10}
      confirmLabel="Resolve"
      busy={resolve.isPending}
      onClose={onClose}
      onConfirm={(justification) => {
        resolve.mutate(
          {
            id: row.id,
            input: {
              outcome,
              justification,
              newHours: outcome === 'Upheld' && newHours ? Number(newHours) : null,
              newSharePercent: outcome === 'Upheld' && newShare ? Number(newShare) : null,
            },
          },
          {
            onSuccess: () => {
              notify(`Dispute on line #${row.id} ${outcome === 'Upheld' ? 'upheld' : 'rejected'}.`);
              onClose();
            },
          },
        );
      }}
    >
      <RadioGroup
        aria-label="Outcome"
        value={outcome}
        onChange={(e) => {
          setOutcome(e.target.value as DisputeOutcome);
        }}
      >
        <FormControlLabel value="Rejected" control={<Radio />} label="Reject – the line stands" />
        <FormControlLabel value="Upheld" control={<Radio />} label="Uphold – cancel, or reduce with new hours/share" />
      </RadioGroup>
      {outcome === 'Upheld' && (
        <Stack direction="row" spacing={2} sx={{ my: 1 }}>
          <TextField
            size="small"
            type="number"
            label={`New hours (now ${row.hours})`}
            value={newHours}
            onChange={(e) => {
              setNewHours(e.target.value);
            }}
            helperText="Leave both empty to cancel the line."
            slotProps={{ htmlInput: { min: 0.01, step: 0.01 } }}
          />
          <TextField
            size="small"
            type="number"
            label={`New share % (now ${row.sharePercent})`}
            value={newShare}
            onChange={(e) => {
              setNewShare(e.target.value);
            }}
            slotProps={{ htmlInput: { min: 0.01, max: 100, step: 0.01 } }}
          />
        </Stack>
      )}
      <ErrorAlert error={resolve.error} />
    </ReasonDialog>
  );
}

export function DeductionQueuePage() {
  const [tab, setTab] = useState(0);
  const { notify } = useToast();
  const drafts = useDeductions({ status: 'Draft', page: 1, pageSize: 100 }, tab === 0);
  const notified = useDeductions({ status: 'Notified', page: 1, pageSize: 100 }, tab === 1);
  const resolved = useDeductions({ status: 'Resolved', page: 1, pageSize: 100 }, tab === 1);
  const disputes = useDeductions({ openDispute: true, page: 1, pageSize: 100 }, tab === 2);
  const notify_ = useNotifyDeductions();
  const approve = useApproveDeductions();
  const cancel = useCancelDeduction();
  const [cancelling, setCancelling] = useState<Deduction | null>(null);
  const [resolving, setResolving] = useState<Deduction | null>(null);

  const readyToApprove = [...(notified.data?.items.filter((d) => d.approvableNow) ?? []), ...(resolved.data?.items ?? [])];
  const stillOpen = notified.data?.items.filter((d) => !d.approvableNow && !d.dispute).length ?? 0;
  const loading = drafts.isLoading || notified.isLoading || resolved.isLoading || disputes.isLoading;

  return (
    <Stack spacing={2}>
      <PageHeader title="Deduction queue" subtitle="Notify employees of draft lines, approve lines once their dispute window has closed, and decide disputes." />
      <Tabs
        value={tab}
        onChange={(_, v: number) => {
          setTab(v);
        }}
        aria-label="Deduction queues"
      >
        <Tab label="Drafts to notify" />
        <Tab label="Ready to approve" />
        <Tab label="Open disputes" />
      </Tabs>
      <ErrorAlert error={drafts.error ?? notified.error ?? resolved.error ?? disputes.error} />
      {loading && <CircularProgress aria-label="Loading deductions" />}
      {tab === 0 && drafts.data && (
        <BatchQueue
          rows={drafts.data.items}
          caption="Draft deduction lines"
          actionLabel="Notify employees"
          emptyText="No draft lines."
          busy={notify_.isPending}
          error={notify_.error}
          onAction={(ids, done) => {
            notify_.mutate(ids, {
              onSuccess: (lines) => {
                notify(`Notified ${lines.length} line(s); dispute windows are open.`);
                done();
              },
            });
          }}
          onCancel={setCancelling}
        />
      )}
      {tab === 1 && notified.data && resolved.data && (
        <>
          {stillOpen > 0 && (
            <Typography variant="body2" color="text.secondary">
              {stillOpen} notified line(s) are still inside their dispute window and are not listed.
            </Typography>
          )}
          <BatchQueue
            rows={readyToApprove}
            caption="Lines ready for approval"
            actionLabel="Approve"
            emptyText="Nothing is ready for approval."
            busy={approve.isPending}
            error={approve.error}
            onAction={(ids, done) => {
              approve.mutate(ids, {
                onSuccess: (lines) => {
                  notify(`Approved ${lines.length} line(s).`);
                  done();
                },
              });
            }}
            onCancel={setCancelling}
          />
        </>
      )}
      {tab === 2 && disputes.data && (
        <DeductionTable
          rows={disputes.data.items}
          caption="Open disputes"
          emptyText="No open disputes."
          actions={(row) => (
            <Button
              size="small"
              variant="outlined"
              aria-label={`Resolve dispute on line ${row.id}`}
              onClick={() => {
                setResolving(row);
              }}
            >
              Resolve
            </Button>
          )}
        />
      )}
      {cancelling && (
        <ReasonDialog
          open
          title={`Cancel line #${cancelling.id}`}
          description={`${cancelling.employeeName}, ${cancelling.taskTitle}. The line will not be deducted.`}
          label="Reason"
          minLength={10}
          confirmLabel="Cancel line"
          busy={cancel.isPending}
          onClose={() => {
            setCancelling(null);
            cancel.reset();
          }}
          onConfirm={(reason) => {
            cancel.mutate(
              { id: cancelling.id, reason },
              {
                onSuccess: () => {
                  notify(`Line #${cancelling.id} cancelled.`);
                  setCancelling(null);
                },
              },
            );
          }}
        >
          <ErrorAlert error={cancel.error} />
        </ReasonDialog>
      )}
      {resolving && (
        <ResolveDialog
          row={resolving}
          onClose={() => {
            setResolving(null);
          }}
        />
      )}
    </Stack>
  );
}
