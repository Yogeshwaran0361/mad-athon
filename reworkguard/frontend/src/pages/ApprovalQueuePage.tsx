import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import {
  Button,
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
  Typography,
} from '@mui/material';
import { useApprovalQueue, useDecideTimeLog } from '../api/hooks';
import { ErrorAlert } from '../components/ErrorAlert';

export function ApprovalQueuePage() {
  const [page, setPage] = useState(1);
  const { data, isLoading, error } = useApprovalQueue(page);
  const decide = useDecideTimeLog();

  return (
    <Stack spacing={2}>
      <Typography variant="h5">Approval queue</Typography>
      <Typography variant="body2" color="text.secondary">
        Pending time logs from your team. Your own logs never appear here.
      </Typography>
      <ErrorAlert error={error ?? decide.error} />
      {isLoading && <CircularProgress />}
      {data && (
        <>
          <TableContainer component={Paper}>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Date</TableCell>
                  <TableCell>Employee</TableCell>
                  <TableCell>Task</TableCell>
                  <TableCell>Type</TableCell>
                  <TableCell>Bug</TableCell>
                  <TableCell align="right">Hours</TableCell>
                  <TableCell>Note</TableCell>
                  <TableCell align="right">Decision</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {data.items.map((log) => (
                  <TableRow key={log.id}>
                    <TableCell>{log.workDate}</TableCell>
                    <TableCell>{log.employeeName}</TableCell>
                    <TableCell>
                      <Link component={RouterLink} to={`/tasks/${log.taskId}`}>
                        {log.taskTitle}
                      </Link>
                    </TableCell>
                    <TableCell>{log.workType}</TableCell>
                    <TableCell>{log.bugId ?? '–'}</TableCell>
                    <TableCell align="right">{log.hours.toFixed(2)}</TableCell>
                    <TableCell>{log.note ?? ''}</TableCell>
                    <TableCell align="right">
                      <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
                        <Button size="small" variant="contained" disabled={decide.isPending} onClick={() => { decide.mutate({ id: log.id, decision: 'Approved' }); }}>
                          Approve
                        </Button>
                        <Button size="small" color="error" disabled={decide.isPending} onClick={() => { decide.mutate({ id: log.id, decision: 'Rejected' }); }}>
                          Reject
                        </Button>
                      </Stack>
                    </TableCell>
                  </TableRow>
                ))}
                {data.items.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={8}>Nothing waiting for approval.</TableCell>
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
