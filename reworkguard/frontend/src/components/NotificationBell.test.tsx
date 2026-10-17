import { screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { mockFetch, renderWithProviders } from '../test/utils';
import { NotificationBell } from './NotificationBell';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('NotificationBell', () => {
  it('puts the unread count in the accessible name and links to the inbox', async () => {
    mockFetch({ '/api/v1/notifications/unread-count': { unread: 3 } });
    renderWithProviders(<NotificationBell />);
    const link = await screen.findByRole('link', { name: 'Notifications, 3 unread' });
    expect(link).toHaveAttribute('href', '/notifications');
  });

  it('says none unread when the count is zero', async () => {
    mockFetch({ '/api/v1/notifications/unread-count': { unread: 0 } });
    renderWithProviders(<NotificationBell />);
    expect(await screen.findByRole('link', { name: 'Notifications, none unread' })).toBeInTheDocument();
  });
});
