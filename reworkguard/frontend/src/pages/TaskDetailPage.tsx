import Grid from '@mui/material/Grid2';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import { useLogTime, useReportBug, useTask, useTransitionTask } from '../api/hooks';
import type { BugReported, TaskDetail, WorkTaskStatus, WorkType } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { AttributionDialog } from '../components/AttributionDialog';
import { ErrorAlert } from '../components/ErrorAlert';
import { ApprovalBadge, BugStatusBadge, TaskStatusBadge } from '../components/StatusBadge';
import { BugReportForm } from '../forms/BugReportForm';
import { TimeLogForm } from '../forms/TimeLogForm';

function allowedWorkTypes(status: WorkTaskStatus): WorkType[] {
  if (status === 'Assigned' || status === 'InProgress') {
    return ['Regular'];
  }
  if (status === 'Reopened' || status === 'UnderFix') {
    return ['Corrective'];
  }
  return [];
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Card variant="outlined">
      <CardContent>
        <Typography variant="h6" gutterBottom>
          {title}
        </Typography>
        {children}
      </CardContent>
    </Card>
  );
}

export function TaskDetailPage() {
  const id = Number(useParams().id);
  const { data, isLoading, error } = useTask(id);

  if (isLoading) {
    return <CircularProgress />;
  }
  if (!data) {
    return <ErrorAlert error={error ?? new Error('Task not found.')} />;
  }
  return <TaskDetailView detail={data} />;
}

function TaskDetailView({ detail }: { detail: TaskDetail }) {
  const { user, hasRole } = useAuth();
  const { task } = detail;
  const transition = useTransitionTask(task.id);
  const reportBug = useReportBug(task.id);
  const logTime = useLogTime();
  const [reason, setReason] = useState('');
  const [reported, setReported] = useState<BugReported | null>(null);
  const [attributionBug, setAttributionBug] = useState<number | null>(null);

  const isAssignee = task.assignees.some((a) => a.employeeId === user?.id);
  const isLeadOrManager = hasRole('TeamLead', 'Manager');
  const transitions = detail.allowedTransitions.filter((t) => (t === 'Closed' ? isLeadOrManager : isLeadOrManager || isAssignee));
  const isChangeRequest = detail.changeRequestId != null;
  const canReportBug = !isChangeRequest && hasRole('QA', 'TeamLead', 'Manager') && (task.status === 'Completed' || task.status === 'Closed');
  const workTypes: WorkType[] = isChangeRequest ? ['Enhancement'] : allowedWorkTypes(task.status);
  const openBugs = detail.bugs.filter((b) => b.status === 'Open').map((b) => ({ id: b.id, description: b.description }));

  return (
    <Stack spacing={3}>
      <Box>
        <Typography variant="h5" component="h1">
          {task.title}
        </Typography>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 1, flexWrap: 'wrap' }}>
          <TaskStatusBadge status={task.status} />
          <Typography variant="body2" color="text.secondary">
            {task.projectName} / {task.moduleName} · committed {task.committedDate}
            {task.actualCompletionDate && ` · completed ${task.actualCompletionDate}`}
            {detail.warrantyEndsOn && ` · warranty until ${detail.warrantyEndsOn}`} · reopened {task.reopenCount}×
          </Typography>
        </Stack>
        <Stack direction="row" spacing={1} sx={{ mt: 1, flexWrap: 'wrap' }}>
          {task.assignees.map((a) => (
            <Chip key={a.employeeId} size="small" label={a.isOriginalDeliverer ? `${a.name} (original)` : a.name} />
          ))}
        </Stack>
      </Box>

      {reported && (
        <Alert
          severity="info"
          onClose={() => {
            setReported(null);
          }}
        >
          Bug #{reported.bug.id} recorded {reported.bug.withinWarranty ? 'inside' : 'outside'} the warranty window (ends {reported.warrantyEndsOn}) and the task
          was reopened. Original deliverers: {reported.originalDeliverers.map((d) => d.name).join(', ')}.
        </Alert>
      )}

      {transitions.length > 0 && (
        <Section title="Move task">
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ alignItems: { sm: 'center' } }}>
            <TextField size="small" label="Reason (optional)" value={reason} onChange={(e) => { setReason(e.target.value); }} sx={{ flexGrow: 1 }} />
            {transitions.map((target) => (
              <Button
                key={target}
                variant="outlined"
                disabled={transition.isPending}
                onClick={() => {
                  transition.mutate({ targetStatus: target, reason: reason.trim() || null }, { onSuccess: () => { setReason(''); } });
                }}
              >
                Move to {target}
              </Button>
            ))}
          </Stack>
          <ErrorAlert error={transition.error} />
        </Section>
      )}

      <Grid container spacing={3}>
        {canReportBug && (
          <Grid size={{ xs: 12, md: 6 }}>
            <Section title="Report a bug">
              <BugReportForm
                completedOn={task.actualCompletionDate}
                onSubmit={async (input) => {
                  setReported(await reportBug.mutateAsync(input));
                }}
              />
            </Section>
          </Grid>
        )}
        {isAssignee && workTypes.length > 0 && (
          <Grid size={{ xs: 12, md: 6 }}>
            <Section title={workTypes.includes('Corrective') ? 'Log corrective time' : 'Log time'}>
              <TimeLogForm
                key={task.status}
                taskId={task.id}
                allowedTypes={workTypes}
                openBugs={openBugs}
                onSubmit={async (input) => {
                  await logTime.mutateAsync(input);
                }}
              />
            </Section>
          </Grid>
        )}
      </Grid>

      <Section title="Bugs">
        {detail.bugs.length === 0 ? (
          <Typography color="text.secondary">No bugs reported.</Typography>
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>#</TableCell>
                <TableCell>Severity</TableCell>
                <TableCell>Description</TableCell>
                <TableCell>Found</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Warranty</TableCell>
                <TableCell>Review</TableCell>
                <TableCell>
                  <span className="visually-hidden">Actions</span>
                </TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {detail.bugs.map((b) => (
                <TableRow key={b.id}>
                  <TableCell>{b.id}</TableCell>
                  <TableCell>{b.severity}</TableCell>
                  <TableCell>{b.description}</TableCell>
                  <TableCell>{b.foundDate}</TableCell>
                  <TableCell>
                    <BugStatusBadge status={b.status} />
                  </TableCell>
                  <TableCell>{b.withinWarranty ? 'Within' : 'Outside'}</TableCell>
                  <TableCell>{b.isReviewed ? `${b.rootCause ?? ''}${b.isDeductible ? ' · deductible' : ' · not deductible'}` : 'Awaiting review'}</TableCell>
                  <TableCell>
                    {b.isReviewed && (
                      <Button
                        size="small"
                        aria-label={`Attribution for bug ${b.id}`}
                        onClick={() => {
                          setAttributionBug(b.id);
                        }}
                      >
                        Attribution
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Section>

      {attributionBug !== null && (
        <AttributionDialog
          bugId={attributionBug}
          canOverride={hasRole('Manager')}
          onClose={() => {
            setAttributionBug(null);
          }}
        />
      )}

      <Section title="Time logs">
        {detail.timeLogs.length === 0 ? (
          <Typography color="text.secondary">No time logged.</Typography>
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Date</TableCell>
                <TableCell>Employee</TableCell>
                <TableCell>Type</TableCell>
                <TableCell>Bug</TableCell>
                <TableCell align="right">Hours</TableCell>
                <TableCell>Approval</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {detail.timeLogs.map((l) => (
                <TableRow key={l.id}>
                  <TableCell>{l.workDate}</TableCell>
                  <TableCell>{l.employeeName}</TableCell>
                  <TableCell>{l.workType}</TableCell>
                  <TableCell>{l.bugId ?? '–'}</TableCell>
                  <TableCell align="right">{l.hours.toFixed(2)}</TableCell>
                  <TableCell>
                    <ApprovalBadge status={l.approvalStatus} />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Section>

      <Section title="State history">
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>When (UTC)</TableCell>
              <TableCell>Change</TableCell>
              <TableCell>By</TableCell>
              <TableCell>Reason</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {detail.history.map((h) => (
              <TableRow key={h.id}>
                <TableCell>{h.changedAtUtc.replace('T', ' ').slice(0, 16)}</TableCell>
                <TableCell>
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <TaskStatusBadge status={h.fromStatus} />
                    <span aria-hidden>→</span>
                    <TaskStatusBadge status={h.toStatus} />
                  </Stack>
                </TableCell>
                <TableCell>{h.changedByName}</TableCell>
                <TableCell>{h.reason ?? '–'}</TableCell>
              </TableRow>
            ))}
            {detail.history.length === 0 && (
              <TableRow>
                <TableCell colSpan={4}>No status changes yet.</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </Section>
    </Stack>
  );
}
