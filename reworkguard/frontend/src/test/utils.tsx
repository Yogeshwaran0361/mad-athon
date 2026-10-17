import type { ReactElement, ReactNode } from 'react';
import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';
import type { Employee, Role } from '../api/types';
import { AuthContext, type AuthState } from '../auth/AuthContext';
import { ToastProvider } from '../components/Toast';

export function makeUser(role: Role, id = 1): Employee {
  return { id, code: `E${id}`, name: `${role} User`, email: `${role.toLowerCase()}@example.test`, role, payMode: 'Monthly', isActive: true };
}

interface RenderOptions {
  role?: Role;
  userId?: number;
  route?: string;
}

export function renderWithProviders(ui: ReactElement, { role = 'Employee', userId = 1, route = '/' }: RenderOptions = {}) {
  const user = makeUser(role, userId);
  const auth: AuthState = {
    user,
    login: vi.fn(),
    logout: vi.fn(),
    hasRole: (...roles: Role[]) => roles.includes(user.role),
  };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={[route]}>
          <ToastProvider>
            <AuthContext.Provider value={auth}>{children}</AuthContext.Provider>
          </ToastProvider>
        </MemoryRouter>
      </QueryClientProvider>
    );
  }
  return { ...render(ui, { wrapper: Wrapper }), user, client };
}

/** Replaces fetch with a router of path prefix → JSON body. */
export function mockFetch(routes: Record<string, unknown>) {
  const fn = vi.fn((input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
    const match = Object.keys(routes)
      .sort((a, b) => b.length - a.length)
      .find((prefix) => url.startsWith(prefix));
    if (!match) {
      return Promise.resolve(new Response(JSON.stringify({ title: 'Not found' }), { status: 404 }));
    }
    return Promise.resolve(new Response(JSON.stringify(routes[match]), { status: 200, headers: { 'Content-Type': 'application/json' } }));
  });
  vi.stubGlobal('fetch', fn);
  return fn;
}
