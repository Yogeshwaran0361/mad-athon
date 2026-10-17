import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Box, Button, Card, CardContent, CircularProgress, Link, Menu, MenuItem, Paper, Stack, Typography } from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { keys, useTasks, useTransitionTask } from '../api/hooks';
import { TASK_STATUSES, type TaskDetail, type TaskSummary, type WorkTaskStatus } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorAlert } from '../components/ErrorAlert';
import { humanize } from '../components/format';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/toastContext';

/** Filters the server's allowed transitions by what this user may do, mirroring the task detail page. */
function permitted(detail: TaskDetail, userId: number | undefined, isLead: boolean): WorkTaskStatus[] {
  const isAssignee = detail.task.assignees.some((a) => a.employeeId === userId);
  return detail.allowedTransitions.filter((t) => (t === 'Closed' ? isLead : isLead || isAssignee));
}

function useTaskDetailLoader() {
  const client = useQueryClient();
  return (id: number) => client.query({ queryKey: keys.task(id), queryFn: () => api<TaskDetail>(`/tasks/${id}`), staleTime: 0 });
}

function TaskCard({ task, onDragStart }: { task: TaskSummary; onDragStart: (task: TaskSummary) => void }) {
  const { user, hasRole } = useAuth();
  const { notify } = useToast();
  const transition = useTransitionTask(task.id);
  const loadDetail = useTaskDetailLoader();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [options, setOptions] = useState<WorkTaskStatus[] | null>(null);
  const [loadError, setLoadError] = useState<unknown>(null);

  const openMenu = async (target: HTMLElement) => {
    setAnchor(target);
    setOptions(null);
    setLoadError(null);
    try {
      const detail = await loadDetail(task.id);
      setOptions(permitted(detail, user?.id, hasRole('TeamLead', 'Manager')));
    } catch (error) {
      setLoadError(error);
      setOptions([]);
    }
  };

  const move = (target: WorkTaskStatus) => {
    setAnchor(null);
    transition.mutate(
      { targetStatus: target, reason: null },
      {
        onSuccess: () => {
          notify(`Moved "${task.title}" to ${humanize(target)}.`);
        },
      },
    );
  };

  const menuId = `move-menu-${task.id}`;
  return (
    <Card
      variant="outlined"
      draggable
      onDragStart={(e) => {
        e.dataTransfer.setData('text/plain', String(task.id));
        onDragStart(task);
      }}
      sx={{ cursor: 'grab' }}
    >
      <CardContent sx={{ p: 1.5, '&:last-child': { pb: 1.5 } }}>
        <Link component={RouterLink} to={`/tasks/${task.id}`} variant="subtitle2">
          {task.title}
        </Link>
        <Typography variant="caption" component="p" color="text.secondary">
          {task.moduleName} · {task.assignees.map((a) => a.name).join(', ')}
          {task.reopenCount > 0 && ` · reopened ${task.reopenCount}×`}
        </Typography>
        <Button
          size="small"
          aria-haspopup="menu"
          aria-controls={anchor ? menuId : undefined}
          aria-expanded={anchor ? true : undefined}
          aria-label={`Move ${task.title}`}
          disabled={transition.isPending}
          onClick={(e) => {
            void openMenu(e.currentTarget);
          }}
          sx={{ mt: 0.5, px: 0 }}
        >
          Move…
        </Button>
        <ErrorAlert error={transition.error ?? loadError} />
      </CardContent>
      <Menu
        id={menuId}
        anchorEl={anchor}
        open={anchor !== null}
        onClose={() => {
          setAnchor(null);
        }}
      >
        {options === null && <MenuItem disabled>Loading…</MenuItem>}
        {options?.length === 0 && <MenuItem disabled>No moves available</MenuItem>}
        {options?.map((target) => (
          <MenuItem
            key={target}
            onClick={() => {
              move(target);
            }}
          >
            Move to {humanize(target)}
          </MenuItem>
        ))}
      </Menu>
    </Card>
  );
}

function Column({ status, tasks, onDrop, onDragStart }: { status: WorkTaskStatus; tasks: TaskSummary[]; onDrop: (status: WorkTaskStatus) => void; onDragStart: (task: TaskSummary) => void }) {
  const [over, setOver] = useState(false);
  const headingId = `col-${status}`;
  return (
    <Paper
      component="section"
      aria-labelledby={headingId}
      variant="outlined"
      onDragOver={(e) => {
        e.preventDefault();
        setOver(true);
      }}
      onDragLeave={() => {
        setOver(false);
      }}
      onDrop={(e) => {
        e.preventDefault();
        setOver(false);
        onDrop(status);
      }}
      sx={{ p: 1, minWidth: 220, flex: '1 0 220px', bgcolor: over ? 'action.hover' : 'background.default', outline: over ? '2px dashed' : 'none', outlineColor: 'primary.main' }}
    >
      <Typography id={headingId} variant="subtitle1" component="h2" sx={{ px: 0.5, mb: 1 }}>
        {humanize(status)} <span aria-label={`${tasks.length} tasks`}>({tasks.length})</span>
      </Typography>
      <Stack spacing={1} component="ul" sx={{ listStyle: 'none', p: 0, m: 0 }}>
        {tasks.map((t) => (
          <li key={t.id}>
            <TaskCard task={t} onDragStart={onDragStart} />
          </li>
        ))}
      </Stack>
    </Paper>
  );
}

export function BoardPage() {
  const { data, isLoading, error } = useTasks(1, '', 100);
  const { user, hasRole } = useAuth();
  const { notify } = useToast();
  const loadDetail = useTaskDetailLoader();
  const client = useQueryClient();
  const [dragged, setDragged] = useState<TaskSummary | null>(null);
  const [dropError, setDropError] = useState<unknown>(null);

  const handleDrop = async (target: WorkTaskStatus) => {
    const task = dragged;
    setDragged(null);
    if (!task || task.status === target) {
      return;
    }
    setDropError(null);
    try {
      const detail = await loadDetail(task.id);
      if (!permitted(detail, user?.id, hasRole('TeamLead', 'Manager')).includes(target)) {
        notify(`"${task.title}" cannot move from ${humanize(detail.task.status)} to ${humanize(target)}.`, 'warning');
        return;
      }
      const updated = await api<TaskDetail>(`/tasks/${task.id}/transitions`, { method: 'POST', body: { targetStatus: target, reason: null } });
      client.setQueryData(keys.task(task.id), updated);
      await client.invalidateQueries({ queryKey: ['tasks'] });
      notify(`Moved "${task.title}" to ${humanize(target)}.`);
    } catch (e) {
      setDropError(e);
    }
  };

  return (
    <Stack spacing={2}>
      <PageHeader title="Board" subtitle="Drag a card to another column, or use its Move menu (keyboard friendly). Reopening only happens by reporting a bug." />
      <ErrorAlert error={error ?? dropError} />
      {isLoading && <CircularProgress aria-label="Loading tasks" />}
      {data && (
        <Box sx={{ display: 'flex', gap: 2, overflowX: 'auto', pb: 1 }}>
          {TASK_STATUSES.map((s) => (
            <Column
              key={s}
              status={s}
              tasks={data.items.filter((t) => t.status === s)}
              onDragStart={setDragged}
              onDrop={(status) => {
                void handleDrop(status);
              }}
            />
          ))}
        </Box>
      )}
      {data && data.totalCount > data.items.length && (
        <Typography variant="body2" color="text.secondary">
          Showing the first {data.items.length} of {data.totalCount} tasks. Use the task list to page through the rest.
        </Typography>
      )}
    </Stack>
  );
}
