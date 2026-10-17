import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { mockFetch, renderWithProviders } from '../test/utils';
import { Layout } from './Layout';
import { navFor } from './navigation';

vi.mock('@microsoft/signalr', () => {
  class Builder {
    withUrl() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return { on: vi.fn(), start: () => Promise.resolve(), stop: () => Promise.resolve() };
    }
  }
  return { HubConnectionBuilder: Builder, LogLevel: { Warning: 3 } };
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('navigation', () => {
  it('shows employees only their own areas', () => {
    const labels = navFor('Employee').map((i) => i.label);
    expect(labels).toEqual(expect.arrayContaining(['Tasks', 'Board', 'My deductions', 'Payslips']));
    expect(labels).not.toContain('Payroll');
    expect(labels).not.toContain('Audit trail');
    expect(labels).not.toContain('Deduction queue');
  });

  it('gives Finance payroll and employees but not deduction decisions', () => {
    const labels = navFor('Finance').map((i) => i.label);
    expect(labels).toEqual(expect.arrayContaining(['Payroll', 'Employees & pay', 'Reports']));
    expect(labels).not.toContain('Deduction queue');
    expect(labels).not.toContain('Jobs & outbox');
  });

  it('gives Admin operations and audit', () => {
    const labels = navFor('Admin').map((i) => i.label);
    expect(labels).toEqual(expect.arrayContaining(['Jobs & outbox', 'Audit trail', 'Settings']));
  });
});

describe('Layout', () => {
  it('renders a skip link, a labelled main navigation and the current page marker', async () => {
    mockFetch({ '/api/v1/notifications/unread-count': { unread: 2 } });
    renderWithProviders(<Layout />, { role: 'Manager', route: '/payroll' });
    expect(screen.getByRole('link', { name: 'Skip to content' })).toHaveAttribute('href', '#main');
    expect(screen.getByRole('main')).toHaveAttribute('id', 'main');
    expect(await screen.findByRole('link', { name: 'Notifications, 2 unread' })).toBeInTheDocument();
    // jsdom has no media queries, so the narrow layout's drawer is used: open it like a keyboard user would.
    await userEvent.click(screen.getByRole('button', { name: 'Open navigation' }));
    const nav = await screen.findByRole('navigation', { name: 'Main' });
    expect(within(nav).getByRole('link', { name: 'Deduction queue' })).toBeInTheDocument();
    expect(within(nav).getByRole('link', { name: 'Payroll' })).toHaveAttribute('aria-current', 'page');
  });
});
