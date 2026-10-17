import { useState } from 'react';
import { Alert, Button, CircularProgress, Pagination, Stack, TextField } from '@mui/material';
import { useAcknowledgeDeduction, useDeductions, useDisputeDeduction } from '../api/hooks';
import { DEDUCTION_STATUSES, type Deduction, type DeductionStatus } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { DeductionTable } from '../components/DeductionTable';
import { ErrorAlert } from '../components/ErrorAlert';
import { PageHeader } from '../components/PageHeader';
import { ReasonDialog } from '../components/ReasonDialog';
import { useToast } from '../components/toastContext';

export function MyDeductionsPage() {
  const { user } = useAuth();
  const { notify } = useToast();
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<DeductionStatus | ''>('');
  const { data, isLoading, error } = useDeductions({ employeeId: user?.id ?? 0, status: status || undefined, page, pageSize: 20 }, user !== null);
  const dispute = useDisputeDeduction();
  const acknowledge = useAcknowledgeDeduction();
  const [disputing, setDisputing] = useState<Deduction | null>(null);

  const canDispute = (d: Deduction) => d.status === 'Notified' && d.disputeWindowOpen && d.dispute === null;
  const canAcknowledge = (d: Deduction) => d.notifiedAtUtc !== null && d.acknowledgedAtUtc === null && d.status !== 'Cancelled' && d.status !== 'Reversed';

  return (
    <Stack spacing={2}>
      <PageHeader title="My deductions" subtitle="Lines raised against your corrective hours. You can dispute a notified line once, while its window is open." />
      <TextField
        select
        size="small"
        label="Status"
        value={status}
        onChange={(e) => {
          setStatus(e.target.value as DeductionStatus | '');
          setPage(1);
        }}
        slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
        sx={{ maxWidth: 220 }}
      >
        <option value="">All statuses</option>
        {DEDUCTION_STATUSES.map((s) => (
          <option key={s} value={s}>
            {s}
          </option>
        ))}
      </TextField>
      <ErrorAlert error={error ?? acknowledge.error} />
      {isLoading && <CircularProgress aria-label="Loading deductions" />}
      {data && (
        <>
          {data.items.some(canDispute) && <Alert severity="info">Some lines are inside their dispute window. Review them before the window closes.</Alert>}
          <DeductionTable
            rows={data.items}
            caption="My deduction lines"
            showEmployee={false}
            emptyText="No deductions."
            actions={(d) => (
              <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
                {canAcknowledge(d) && (
                  <Button
                    size="small"
                    aria-label={`Acknowledge line ${d.id}`}
                    disabled={acknowledge.isPending}
                    onClick={() => {
                      acknowledge.mutate(d.id, {
                        onSuccess: () => {
                          notify(`Line #${d.id} acknowledged.`);
                        },
                      });
                    }}
                  >
                    Acknowledge
                  </Button>
                )}
                {d.acknowledgedAtUtc && <span>Acknowledged</span>}
                {canDispute(d) && (
                  <Button
                    size="small"
                    variant="outlined"
                    color="warning"
                    aria-label={`Dispute line ${d.id}`}
                    onClick={() => {
                      setDisputing(d);
                    }}
                  >
                    Dispute
                  </Button>
                )}
              </Stack>
            )}
          />
          {data.totalPages > 1 && (
            <Pagination
              count={data.totalPages}
              page={page}
              onChange={(_, p) => {
                setPage(p);
              }}
            />
          )}
        </>
      )}
      {disputing && (
        <ReasonDialog
          open
          title={`Dispute line #${disputing.id}`}
          description="Explain why this deduction is wrong. You can only dispute a line once; a Manager will decide."
          label="Reason"
          minLength={10}
          confirmLabel="Raise dispute"
          busy={dispute.isPending}
          onClose={() => {
            setDisputing(null);
            dispute.reset();
          }}
          onConfirm={(reason) => {
            dispute.mutate(
              { id: disputing.id, reason },
              {
                onSuccess: () => {
                  notify(`Dispute raised on line #${disputing.id}.`);
                  setDisputing(null);
                },
              },
            );
          }}
        >
          <ErrorAlert error={dispute.error} />
        </ReasonDialog>
      )}
    </Stack>
  );
}
