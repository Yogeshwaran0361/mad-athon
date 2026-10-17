import { useState } from 'react';
import {
  Button,
  Card,
  CardContent,
  CircularProgress,
  Pagination,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import { useDashboardSession, useJobs, useOutbox, useRetryOutbox, useTriggerJob } from '../../api/hooks';
import type { OutboxStatus } from '../../api/types';
import { ErrorAlert } from '../../components/ErrorAlert';
import { formatUtc, humanize } from '../../components/format';
import { PageHeader } from '../../components/PageHeader';
import { useToast } from '../../components/toastContext';

const OUTBOX_STATUSES: OutboxStatus[] = ['Pending', 'Sent', 'Skipped', 'DeadLettered'];

function JobsCard() {
  const { data, isLoading, error } = useJobs();
  const trigger = useTriggerJob();
  const session = useDashboardSession();
  const { notify } = useToast();
  return (
    <Card variant="outlined">
      <CardContent>
        <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
          <Typography variant="h6" component="h2">
            Scheduled jobs
          </Typography>
          <Button
            variant="outlined"
            disabled={session.isPending}
            onClick={() => {
              // Open the tab synchronously so pop-up blockers allow it, then point it at the dashboard once the cookie is set.
              const tab = window.open('about:blank', '_blank');
              session.mutate(undefined, {
                onSuccess: () => {
                  if (tab) {
                    tab.opener = null;
                    tab.location.href = '/hangfire';
                  } else {
                    window.location.assign('/hangfire');
                  }
                },
                onError: () => {
                  tab?.close();
                },
              });
            }}
          >
            Open Hangfire dashboard
          </Button>
        </Stack>
        <ErrorAlert error={error ?? trigger.error ?? session.error} />
        {isLoading && <CircularProgress aria-label="Loading jobs" />}
        <TableContainer>
          <Table size="small" aria-label="Scheduled jobs">
            <TableHead>
              <TableRow>
                <TableCell>Job</TableCell>
                <TableCell>Schedule</TableCell>
                <TableCell>Next run</TableCell>
                <TableCell>Last run</TableCell>
                <TableCell>Last state</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data?.map((j) => (
                <TableRow key={j.id}>
                  <TableCell>{j.id}</TableCell>
                  <TableCell>
                    <code>{j.cron}</code> ({j.timeZone})
                  </TableCell>
                  <TableCell>{formatUtc(j.nextRunUtc)}</TableCell>
                  <TableCell>{formatUtc(j.lastRunUtc)}</TableCell>
                  <TableCell>
                    {j.lastState ?? '–'}
                    {j.error && (
                      <Typography variant="caption" color="error" component="div">
                        Error: {j.error}
                      </Typography>
                    )}
                  </TableCell>
                  <TableCell align="right">
                    <Button
                      size="small"
                      aria-label={`Run ${j.id} now`}
                      disabled={trigger.isPending}
                      onClick={() => {
                        trigger.mutate(j.id, {
                          onSuccess: (r) => {
                            notify(`Queued ${j.id} (job ${r.jobId}).`);
                          },
                        });
                      }}
                    >
                      Run now
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </CardContent>
    </Card>
  );
}

function OutboxCard() {
  const [status, setStatus] = useState<OutboxStatus | ''>('DeadLettered');
  const [page, setPage] = useState(1);
  const { data, isLoading, error } = useOutbox(status, page);
  const retry = useRetryOutbox();
  const { notify } = useToast();
  return (
    <Card variant="outlined">
      <CardContent>
        <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
          <Typography variant="h6" component="h2">
            Delivery outbox
          </Typography>
          <TextField
            select
            size="small"
            label="Status"
            value={status}
            onChange={(e) => {
              setStatus(e.target.value as OutboxStatus | '');
              setPage(1);
            }}
            slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
          >
            <option value="">All</option>
            {OUTBOX_STATUSES.map((s) => (
              <option key={s} value={s}>
                {humanize(s)}
              </option>
            ))}
          </TextField>
        </Stack>
        <ErrorAlert error={error ?? retry.error} />
        {isLoading && <CircularProgress aria-label="Loading outbox" />}
        <TableContainer>
          <Table size="small" aria-label="Outbox messages">
            <TableHead>
              <TableRow>
                <TableCell>#</TableCell>
                <TableCell>Type</TableCell>
                <TableCell>Channel</TableCell>
                <TableCell>Recipient</TableCell>
                <TableCell>Status</TableCell>
                <TableCell align="right">Attempts</TableCell>
                <TableCell>Next attempt</TableCell>
                <TableCell>Last error</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data?.items.map((m) => (
                <TableRow key={m.id}>
                  <TableCell>{m.id}</TableCell>
                  <TableCell>{humanize(m.type)}</TableCell>
                  <TableCell>{m.channel}</TableCell>
                  <TableCell>#{m.recipientEmployeeId}</TableCell>
                  <TableCell>{humanize(m.status)}</TableCell>
                  <TableCell align="right">{m.attempts}</TableCell>
                  <TableCell>{formatUtc(m.nextAttemptAtUtc)}</TableCell>
                  <TableCell>{m.lastError ?? '–'}</TableCell>
                  <TableCell align="right">
                    {m.status === 'DeadLettered' && (
                      <Button
                        size="small"
                        aria-label={`Retry message ${m.id}`}
                        disabled={retry.isPending}
                        onClick={() => {
                          retry.mutate(m.id, {
                            onSuccess: () => {
                              notify(`Message #${m.id} queued for retry.`);
                            },
                          });
                        }}
                      >
                        Retry
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
              {data?.items.length === 0 && (
                <TableRow>
                  <TableCell colSpan={9}>No messages.</TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
        {data && data.totalPages > 1 && (
          <Pagination
            count={data.totalPages}
            page={page}
            onChange={(_, p) => {
              setPage(p);
            }}
          />
        )}
      </CardContent>
    </Card>
  );
}

export function OperationsPage() {
  return (
    <Stack spacing={2}>
      <PageHeader title="Jobs & outbox" subtitle="Background jobs and notification delivery." />
      <JobsCard />
      <OutboxCard />
    </Stack>
  );
}
