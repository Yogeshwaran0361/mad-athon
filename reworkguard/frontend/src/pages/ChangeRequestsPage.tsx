import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import {
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Link,
  Paper,
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
import { useApproveChangeRequest, useChangeRequests, useCloseChangeRequest, useCreateChangeRequest } from '../api/hooks';
import type { ChangeRequest } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorAlert } from '../components/ErrorAlert';

const EMPTY = { projectId: '', moduleId: '', title: '', customerName: '', clientReference: '', quotedAmountExTax: '' };

/** Customer feature requests: paid new work that never enters the bug, reopen or deduction flow. */
export function ChangeRequestsPage() {
  const { hasRole } = useAuth();
  const { data, isLoading, error } = useChangeRequests();
  const create = useCreateChangeRequest();
  const [form, setForm] = useState(EMPTY);
  const [approving, setApproving] = useState<ChangeRequest | null>(null);
  const [closing, setClosing] = useState<ChangeRequest | null>(null);
  const canCreate = hasRole('QA', 'TeamLead', 'Manager');
  const isManager = hasRole('Manager');
  const isFinance = hasRole('Finance');

  const field = (name: keyof typeof EMPTY, label: string, type = 'text') => (
    <TextField
      size="small"
      label={label}
      type={type}
      value={form[name]}
      onChange={(e) => { setForm({ ...form, [name]: e.target.value }); }}
      required
    />
  );

  return (
    <Stack spacing={2}>
      <Typography variant="h5">Feature requests</Typography>
      <Typography variant="body2" color="text.secondary">
        Additional features a customer asks for after delivery. They are paid work: the original task is never reopened and nothing is deducted.
      </Typography>
      <ErrorAlert error={error ?? create.error} />
      {canCreate && (
        <Paper sx={{ p: 2 }}>
          <Typography variant="subtitle1" gutterBottom>New feature request</Typography>
          <Stack
            component="form"
            direction={{ xs: 'column', md: 'row' }}
            spacing={1}
            sx={{ flexWrap: 'wrap' }}
            onSubmit={(e) => {
              e.preventDefault();
              create.mutate(
                {
                  projectId: Number(form.projectId),
                  moduleId: Number(form.moduleId),
                  title: form.title,
                  customerName: form.customerName,
                  clientReference: form.clientReference,
                  quotedAmountExTax: Number(form.quotedAmountExTax),
                },
                { onSuccess: () => { setForm(EMPTY); } },
              );
            }}
          >
            {field('projectId', 'Project ID', 'number')}
            {field('moduleId', 'Module ID', 'number')}
            {field('title', 'Title')}
            {field('customerName', 'Customer')}
            {field('clientReference', 'Client reference')}
            {field('quotedAmountExTax', 'Quoted amount (ex tax)', 'number')}
            <Button type="submit" variant="contained" disabled={create.isPending}>Submit</Button>
          </Stack>
        </Paper>
      )}
      {isLoading && <CircularProgress />}
      {data && (
        <TableContainer component={Paper}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Ref</TableCell>
                <TableCell>Title</TableCell>
                <TableCell>Project / module</TableCell>
                <TableCell>Customer</TableCell>
                <TableCell align="right">Quoted</TableCell>
                <TableCell align="right">Approved h</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Task</TableCell>
                <TableCell align="right">Action</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.map((cr) => (
                <TableRow key={cr.id}>
                  <TableCell>{cr.clientReference}</TableCell>
                  <TableCell>{cr.title}</TableCell>
                  <TableCell>{cr.projectName} / {cr.moduleName}</TableCell>
                  <TableCell>{cr.customerName}</TableCell>
                  <TableCell align="right">{cr.quotedAmountExTax?.toFixed(2) ?? '–'}</TableCell>
                  <TableCell align="right">{cr.approvedHours.toFixed(2)}</TableCell>
                  <TableCell>{cr.status}</TableCell>
                  <TableCell>
                    {cr.taskId ? <Link component={RouterLink} to={`/tasks/${cr.taskId}`}>#{cr.taskId}</Link> : '–'}
                  </TableCell>
                  <TableCell align="right">
                    {isManager && cr.status === 'Requested' && (
                      <Button size="small" variant="contained" onClick={() => { setApproving(cr); }}>Approve</Button>
                    )}
                    {isFinance && cr.status === 'Completed' && (
                      <Button size="small" variant="contained" onClick={() => { setClosing(cr); }}>Close &amp; pay out</Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
              {data.length === 0 && (
                <TableRow>
                  <TableCell colSpan={9}>No feature requests yet.</TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      )}
      {approving && <ApproveDialog cr={approving} onClose={() => { setApproving(null); }} />}
      {closing && <CloseDialog cr={closing} onClose={() => { setClosing(null); }} />}
    </Stack>
  );
}

/** Finance records what the customer paid; the API then computes the profit share and creates the awards. */
function CloseDialog({ cr, onClose }: { cr: ChangeRequest; onClose: () => void }) {
  const close = useCloseChangeRequest();
  const [received, setReceived] = useState(cr.quotedAmountExTax?.toString() ?? '');
  return (
    <Dialog open onClose={onClose}>
      <DialogTitle>Close “{cr.title}”</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <ErrorAlert error={close.error} />
          <TextField
            label="Amount received (ex tax)"
            type="number"
            value={received}
            onChange={(e) => { setReceived(e.target.value); }}
            helperText="Filled with the quoted amount. Profit-share awards are created automatically on close."
            required
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={close.isPending || received === ''}
          onClick={() => {
            close.mutate({ id: cr.id, receivedAmountExTax: Number(received) }, { onSuccess: onClose });
          }}
        >
          Close &amp; pay out
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function ApproveDialog({ cr, onClose }: { cr: ChangeRequest; onClose: () => void }) {
  const approve = useApproveChangeRequest();
  const [hours, setHours] = useState('');
  const [assignees, setAssignees] = useState('');
  return (
    <Dialog open onClose={onClose}>
      <DialogTitle>Approve “{cr.title}”</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <ErrorAlert error={approve.error} />
          <TextField label="Approved hours" type="number" value={hours} onChange={(e) => { setHours(e.target.value); }} required />
          <TextField
            label="Assignee employee IDs"
            helperText="Comma separated; a new task is created for them"
            value={assignees}
            onChange={(e) => { setAssignees(e.target.value); }}
            required
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={approve.isPending}
          onClick={() => {
            const assigneeIds = assignees.split(',').map((s) => Number(s.trim())).filter((n) => n > 0);
            approve.mutate({ id: cr.id, input: { approvedHours: Number(hours), assigneeIds, committedDate: null } }, { onSuccess: onClose });
          }}
        >
          Approve
        </Button>
      </DialogActions>
    </Dialog>
  );
}
