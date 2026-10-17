import { Badge, IconButton, Tooltip } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import { useUnreadCount } from '../api/hooks';

function BellIcon() {
  return (
    <svg width="24" height="24" viewBox="0 0 24 24" aria-hidden="true" focusable="false" fill="currentColor">
      <path d="M12 22a2 2 0 0 0 2-2h-4a2 2 0 0 0 2 2zm6-6V11a6 6 0 0 0-5-5.91V4a1 1 0 0 0-2 0v1.09A6 6 0 0 0 6 11v5l-2 2v1h16v-1l-2-2z" />
    </svg>
  );
}

/** Links to the inbox; the unread count is part of the accessible name, not only the red badge. */
export function NotificationBell() {
  const { data } = useUnreadCount(true);
  const unread = data?.unread ?? 0;
  const label = unread === 0 ? 'Notifications, none unread' : `Notifications, ${unread} unread`;
  return (
    <Tooltip title={label}>
      <IconButton color="inherit" component={RouterLink} to="/notifications" aria-label={label}>
        <Badge badgeContent={unread} color="error" max={99}>
          <BellIcon />
        </Badge>
      </IconButton>
    </Tooltip>
  );
}
