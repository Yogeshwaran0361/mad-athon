import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { api, setUnauthorizedHandler, tokenStore } from '../api/client';
import type { Employee, LoginResponse, Role } from '../api/types';
import { AuthContext, type AuthState } from './AuthContext';

const USER_KEY = 'reworkguard.user';

function storedUser(): Employee | null {
  const raw = sessionStorage.getItem(USER_KEY);
  return raw && tokenStore.get() ? (JSON.parse(raw) as Employee) : null;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<Employee | null>(storedUser);
  const queryClient = useQueryClient();

  const logout = useCallback(() => {
    // Revokes the refresh cookie's session on the server; local state is cleared either way.
    void fetch('/api/v1/auth/logout', { method: 'POST', credentials: 'same-origin' }).catch(() => undefined);
    tokenStore.clear();
    sessionStorage.removeItem(USER_KEY);
    queryClient.clear();
    setUser(null);
  }, [queryClient]);

  useEffect(() => {
    setUnauthorizedHandler(logout);
  }, [logout]);

  const login = useCallback(async (email: string, password: string) => {
    const response = await api<LoginResponse>('/auth/login', { method: 'POST', body: { email, password } });
    tokenStore.set(response.accessToken);
    sessionStorage.setItem(USER_KEY, JSON.stringify(response.employee));
    setUser(response.employee);
  }, []);

  const value = useMemo<AuthState>(
    () => ({
      user,
      login,
      logout,
      hasRole: (...roles: Role[]) => user !== null && roles.includes(user.role),
    }),
    [user, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
