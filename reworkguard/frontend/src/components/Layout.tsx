import { useState } from 'react';
import {
  AppBar,
  Box,
  Button,
  Divider,
  Drawer,
  IconButton,
  List,
  ListItem,
  ListItemButton,
  ListItemText,
  ListSubheader,
  Toolbar,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { NotificationBell } from './NotificationBell';
import { navFor, type NavItem } from './navigation';
import { useNotificationHub } from './useNotificationHub';

const DRAWER_WIDTH = 230;

function NavList({ items, onNavigate }: { items: NavItem[]; onNavigate: () => void }) {
  const location = useLocation();
  const groups = [...new Set(items.map((i) => i.group))];
  return (
    <nav aria-label="Main">
      {groups.map((group) => (
        <List key={group} dense subheader={<ListSubheader component="li">{group}</ListSubheader>}>
          {items
            .filter((i) => i.group === group)
            .map((item) => {
              const active = location.pathname === item.to || (item.to !== '/admin' && location.pathname.startsWith(`${item.to}/`));
              return (
                <ListItem key={item.to} disablePadding>
                  <ListItemButton component={NavLink} to={item.to} end selected={active} aria-current={active ? 'page' : undefined} onClick={onNavigate}>
                    <ListItemText primary={item.label} />
                  </ListItemButton>
                </ListItem>
              );
            })}
        </List>
      ))}
    </nav>
  );
}

export function Layout() {
  const { user, logout } = useAuth();
  const theme = useTheme();
  const wide = useMediaQuery(theme.breakpoints.up('md'));
  const [open, setOpen] = useState(false);
  const items = navFor(user?.role);
  useNotificationHub(user !== null);

  const drawer = (
    <>
      <Toolbar />
      <Divider />
      <NavList
        items={items}
        onNavigate={() => {
          setOpen(false);
        }}
      />
    </>
  );

  return (
    <Box sx={{ display: 'flex' }}>
      <a href="#main" className="skip-link" style={skipLink}>
        Skip to content
      </a>
      <AppBar position="fixed" color="primary" sx={{ zIndex: (t) => t.zIndex.drawer + 1 }}>
        <Toolbar sx={{ gap: 1 }}>
          {!wide && (
            <IconButton
              color="inherit"
              edge="start"
              aria-label="Open navigation"
              aria-expanded={open}
              onClick={() => {
                setOpen(true);
              }}
            >
              <svg width="24" height="24" viewBox="0 0 24 24" aria-hidden="true" fill="currentColor">
                <path d="M3 6h18v2H3zm0 5h18v2H3zm0 5h18v2H3z" />
              </svg>
            </IconButton>
          )}
          <Typography variant="h6" component="span" sx={{ mr: 2 }}>
            ReworkGuard
          </Typography>
          <Box sx={{ flexGrow: 1 }} />
          <NotificationBell />
          <Typography variant="body2" sx={{ mx: 1, display: { xs: 'none', sm: 'block' } }}>
            {user?.name} · {user?.role}
          </Typography>
          <Button color="inherit" onClick={logout}>
            Sign out
          </Button>
        </Toolbar>
      </AppBar>
      {wide ? (
        <Drawer variant="permanent" sx={{ width: DRAWER_WIDTH, flexShrink: 0, '& .MuiDrawer-paper': { width: DRAWER_WIDTH } }}>
          {drawer}
        </Drawer>
      ) : (
        <Drawer
          variant="temporary"
          open={open}
          onClose={() => {
            setOpen(false);
          }}
          ModalProps={{ keepMounted: true }}
          sx={{ '& .MuiDrawer-paper': { width: DRAWER_WIDTH } }}
        >
          {drawer}
        </Drawer>
      )}
      <Box component="main" id="main" tabIndex={-1} sx={{ flexGrow: 1, p: { xs: 2, md: 3 }, minWidth: 0, outline: 'none' }}>
        <Toolbar />
        <Outlet />
      </Box>
    </Box>
  );
}

const skipLink: React.CSSProperties = {
  position: 'absolute',
  left: 8,
  top: -48,
  zIndex: 2000,
  background: '#fff',
  color: '#1d4e89',
  padding: '8px 12px',
};
