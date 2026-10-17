import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { tokenStore } from '../api/client';
import type { AppNotification } from '../api/types';
import { useToast } from './toastContext';

/**
 * Connects to the notifications hub while signed in. The token travels as the access_token query parameter, which the
 * SignalR client adds for WebSocket and SSE transports. Each pushed notification refreshes the inbox and unread count
 * and is announced through the toast live region.
 */
export function useNotificationHub(enabled: boolean) {
  const client = useQueryClient();
  const { notify } = useToast();

  useEffect(() => {
    if (!enabled) {
      return undefined;
    }
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/notifications', { accessTokenFactory: () => tokenStore.get() ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('notificationReceived', (notification: AppNotification) => {
      void client.invalidateQueries({ queryKey: ['notifications'] });
      notify(`New notification: ${notification.title}`, 'info');
    });

    let stopped = false;
    connection.start().catch(() => {
      // The bell still polls the unread count, so a failed live connection only delays updates.
      if (!stopped) {
        void client.invalidateQueries({ queryKey: ['notifications'] });
      }
    });

    return () => {
      stopped = true;
      void connection.stop();
    };
  }, [enabled, client, notify]);
}
