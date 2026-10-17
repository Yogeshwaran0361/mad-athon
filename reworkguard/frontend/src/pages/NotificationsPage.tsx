import { useEffect, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import {
  Button,
  Card,
  CardContent,
  Checkbox,
  CircularProgress,
  FormControlLabel,
  Link,
  List,
  ListItem,
  ListItemText,
  Pagination,
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useMarkNotificationRead, useNotificationPreferences, useNotifications, useUpdateNotificationPreferences } from '../api/hooks';
import type { AppNotification, NotificationPreference } from '../api/types';
import { ErrorAlert } from '../components/ErrorAlert';
import { formatUtc, humanize } from '../components/format';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/toastContext';

/** Only in-app links are followed; anything else is shown as text. */
function safeLink(link: string | null): string | null {
  return link?.startsWith('/') && !link.startsWith('//') ? link : null;
}

function NotificationItem({ n, onRead, busy }: { n: AppNotification; onRead: (id: number) => void; busy: boolean }) {
  const unread = n.readAtUtc === null;
  const link = safeLink(n.link);
  return (
    <ListItem
      divider
      secondaryAction={
        unread ? (
          <Button
            size="small"
            disabled={busy}
            aria-label={`Mark "${n.title}" as read`}
            onClick={() => {
              onRead(n.id);
            }}
          >
            Mark read
          </Button>
        ) : undefined
      }
      sx={{ bgcolor: unread ? 'action.hover' : undefined, pr: 14 }}
    >
      <ListItemText
        primary={
          <>
            {unread && <strong>Unread · </strong>}
            {link ? (
              <Link
                component={RouterLink}
                to={link}
                onClick={() => {
                  if (unread) {
                    onRead(n.id);
                  }
                }}
              >
                {n.title}
              </Link>
            ) : (
              n.title
            )}
          </>
        }
        secondary={
          <>
            {n.body}
            <br />
            {humanize(n.type)} · {formatUtc(n.createdAtUtc)}
          </>
        }
      />
    </ListItem>
  );
}

function Preferences() {
  const { data, isLoading, error } = useNotificationPreferences();
  const update = useUpdateNotificationPreferences();
  const { notify } = useToast();
  const [draft, setDraft] = useState<NotificationPreference[]>([]);
  useEffect(() => {
    if (data) {
      setDraft(data);
    }
  }, [data]);

  return (
    <Card variant="outlined">
      <CardContent>
        <Typography variant="h6" component="h2" gutterBottom>
          Email preferences
        </Typography>
        <Typography variant="body2" color="text.secondary" gutterBottom>
          In-app notifications are always on. Formal notices are always emailed.
        </Typography>
        <ErrorAlert error={error ?? update.error} />
        {isLoading && <CircularProgress aria-label="Loading preferences" />}
        {draft.length > 0 && (
          <form
            onSubmit={(e) => {
              e.preventDefault();
              update.mutate(
                draft.filter((p) => !p.emailMandatory).map((p) => ({ type: p.type, emailEnabled: p.emailEnabled })),
                {
                  onSuccess: () => {
                    notify('Preferences saved.');
                  },
                },
              );
            }}
          >
            <Table size="small" aria-label="Email preferences">
              <TableHead>
                <TableRow>
                  <TableCell>Notification</TableCell>
                  <TableCell>Email</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {draft.map((p) => (
                  <TableRow key={p.type}>
                    <TableCell>{humanize(p.type)}</TableCell>
                    <TableCell>
                      <FormControlLabel
                        control={
                          <Switch
                            checked={p.emailEnabled || p.emailMandatory}
                            disabled={p.emailMandatory}
                            onChange={(e) => {
                              setDraft(draft.map((d) => (d.type === p.type ? { ...d, emailEnabled: e.target.checked } : d)));
                            }}
                            slotProps={{ input: { 'aria-label': `Email for ${humanize(p.type)}` } }}
                          />
                        }
                        label={p.emailMandatory ? 'Always (formal notice)' : p.emailEnabled ? 'On' : 'Off'}
                      />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <Button type="submit" variant="contained" sx={{ mt: 2 }} disabled={update.isPending}>
              Save preferences
            </Button>
          </form>
        )}
      </CardContent>
    </Card>
  );
}

export function NotificationsPage() {
  const [page, setPage] = useState(1);
  const [unreadOnly, setUnreadOnly] = useState(false);
  const { data, isLoading, error } = useNotifications(page, unreadOnly);
  const markRead = useMarkNotificationRead();

  return (
    <Stack spacing={3}>
      <PageHeader
        title="Notifications"
        actions={
          <>
            <FormControlLabel
              control={
                <Checkbox
                  checked={unreadOnly}
                  onChange={(e) => {
                    setUnreadOnly(e.target.checked);
                    setPage(1);
                  }}
                />
              }
              label="Unread only"
            />
            <Button
              variant="outlined"
              disabled={markRead.isPending}
              onClick={() => {
                markRead.mutate('all');
              }}
            >
              Mark all read
            </Button>
          </>
        }
      />
      <ErrorAlert error={error ?? markRead.error} />
      {isLoading && <CircularProgress aria-label="Loading notifications" />}
      {data && (
        <Card variant="outlined">
          <List aria-label="Inbox">
            {data.items.map((n) => (
              <NotificationItem
                key={n.id}
                n={n}
                busy={markRead.isPending}
                onRead={(id) => {
                  markRead.mutate(id);
                }}
              />
            ))}
            {data.items.length === 0 && (
              <ListItem>
                <ListItemText primary={unreadOnly ? 'No unread notifications.' : 'No notifications yet.'} />
              </ListItem>
            )}
          </List>
        </Card>
      )}
      {data && data.totalPages > 1 && (
        <Pagination
          count={data.totalPages}
          page={page}
          onChange={(_, p) => {
            setPage(p);
          }}
        />
      )}
      <Preferences />
    </Stack>
  );
}
