import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { Alert } from '@mui/material';
import type { Role } from '../api/types';
import { useAuth } from './AuthContext';

export function RequireAuth({ roles }: { roles?: Role[] }) {
  const { user, hasRole } = useAuth();
  const location = useLocation();

  if (!user) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }
  if (roles && !hasRole(...roles)) {
    return <Alert severity="warning">You do not have access to this page.</Alert>;
  }
  return <Outlet />;
}
