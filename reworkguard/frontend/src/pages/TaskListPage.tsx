import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import {
  CircularProgress,
  Link,
  Pagination,
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
import { useTasks } from '../api/hooks';
import type { WorkTaskStatus } from '../api/types';
import { ErrorAlert } from '../components/ErrorAlert';
import { TaskStatusBadge } from '../components/StatusBadge';

const STATUSES: WorkTaskStatus[] = ['Assigned', 'InProgress', 'Completed', 'Reopened', 'UnderFix', 'Closed'];

export function TaskListPage() {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<WorkTaskStatus | ''>('');
  const { data, isLoading, error } = useTasks(page, status);

  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="h5">Tasks</Typography>
        <TextField
          select
          size="small"
          label="Status"
          value={status}
          onChange={(e) => {
            setStatus(e.target.value as WorkTaskStatus | '');
            setPage(1);
          }}
          slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
          sx={{ minWidth: 180 }}
        >
          <option value="">All statuses</option>
          {STATUSES.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </TextField>
      </Stack>
      <ErrorAlert error={error} />
      {isLoading && <CircularProgress />}
      {data && (
        <>
          <TableContainer component={Paper}>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Task</TableCell>
                  <TableCell>Module</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell>Assignees</TableCell>
                  <TableCell>Committed</TableCell>
                  <TableCell>Completed</TableCell>
                  <TableCell align="right">Reopens</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {data.items.map((task) => (
                  <TableRow key={task.id} hover>
                    <TableCell>
                      <Link component={RouterLink} to={`/tasks/${task.id}`}>
                        {task.title}
                      </Link>
                    </TableCell>
                    <TableCell>
                      {task.projectName} / {task.moduleName}
                    </TableCell>
                    <TableCell>
                      <TaskStatusBadge status={task.status} />
                    </TableCell>
                    <TableCell>{task.assignees.map((a) => a.name).join(', ')}</TableCell>
                    <TableCell>{task.committedDate}</TableCell>
                    <TableCell>{task.actualCompletionDate ?? '–'}</TableCell>
                    <TableCell align="right">{task.reopenCount}</TableCell>
                  </TableRow>
                ))}
                {data.items.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={7}>No tasks match this filter.</TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </TableContainer>
          {data.totalPages > 1 && <Pagination count={data.totalPages} page={page} onChange={(_, p) => { setPage(p); }} />}
        </>
      )}
    </Stack>
  );
}
