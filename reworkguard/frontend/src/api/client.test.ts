import { afterEach, describe, expect, it, vi } from 'vitest';
import { api, setUnauthorizedHandler, tokenStore } from './client';

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('api token refresh', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    tokenStore.clear();
  });

  it('refreshes once on 401 and retries with the new token', async () => {
    tokenStore.set('old');
    const calls: { url: string; auth?: string }[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string, init: RequestInit = {}) => {
        const auth = (init.headers as Record<string, string> | undefined)?.Authorization;
        calls.push({ url, auth });
        if (url === '/api/v1/auth/refresh') return Promise.resolve(json(200, { accessToken: 'new' }));
        return Promise.resolve(auth === 'Bearer new' ? json(200, { ok: true }) : json(401, { title: 'Unauthorized' }));
      }),
    );

    await expect(api<{ ok: boolean }>('/tasks')).resolves.toEqual({ ok: true });
    expect(calls.map((c) => c.url)).toEqual(['/api/v1/tasks', '/api/v1/auth/refresh', '/api/v1/tasks']);
    expect(tokenStore.get()).toBe('new');
  });

  it('shares one refresh between concurrent requests and signs out when it fails', async () => {
    tokenStore.set('old');
    const onUnauthorized = vi.fn();
    setUnauthorizedHandler(onUnauthorized);
    const fetchMock = vi.fn((url: string) =>
      Promise.resolve(url === '/api/v1/auth/refresh' ? json(401, {}) : json(401, { title: 'Unauthorized' })),
    );
    vi.stubGlobal('fetch', fetchMock);

    await Promise.allSettled([api('/a'), api('/b')]);

    expect(fetchMock.mock.calls.filter(([url]) => url === '/api/v1/auth/refresh')).toHaveLength(1);
    expect(onUnauthorized).toHaveBeenCalled();
  });
});
