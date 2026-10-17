import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { request, type APIRequestContext } from '@playwright/test';
import { apiUrl, password, statePath, todayUtc, users, type E2eState, type UserKey } from './support/env';

interface TaskSummary {
  id: number;
  title: string;
  moduleId: number;
  moduleName: string;
}

async function client(who: UserKey): Promise<{ api: APIRequestContext; id: number }> {
  const anon = await request.newContext({ baseURL: apiUrl });
  const res = await anon.post('/api/v1/auth/login', { data: { email: users[who], password: password() } });
  if (!res.ok()) {
    throw new Error(`login ${who} failed: ${res.status()} ${await res.text()}`);
  }
  const body = (await res.json()) as { accessToken: string; employee: { id: number } };
  await anon.dispose();
  const api = await request.newContext({ baseURL: apiUrl, extraHTTPHeaders: { Authorization: `Bearer ${body.accessToken}` } });
  return { api, id: body.employee.id };
}

async function call<T>(api: APIRequestContext, method: 'get' | 'post', path: string, data?: unknown): Promise<T> {
  const res = method === 'get' ? await api.get(path) : await api.post(path, { data: data ?? {} });
  if (!res.ok()) {
    throw new Error(`${method.toUpperCase()} ${path} failed: ${res.status()} ${await res.text()}`);
  }
  return (await res.json()) as T;
}

/** Creates the journeys' data through the public API, on top of the Development demo seed. */
export default async function globalSetup(): Promise<void> {
  const lead = await client('lead');
  const priya = await client('priya');
  const today = todayUtc();
  const stamp = Date.now().toString(36);

  const tasks = await call<{ items: TaskSummary[] }>(lead.api, 'get', '/api/v1/tasks?pageSize=100');
  const checkout = tasks.items.find((t) => t.moduleName === 'Checkout') ?? tasks.items[0];
  if (!checkout) {
    throw new Error('No seeded module found; is the Development seed enabled?');
  }

  const create = (title: string) =>
    call<TaskSummary>(lead.api, 'post', '/api/v1/tasks', { moduleId: checkout.moduleId, title, committedDate: today, assigneeIds: [priya.id] });
  const move = (taskId: number, targetStatus: string) =>
    call(priya.api, 'post', `/api/v1/tasks/${taskId}/transitions`, { targetStatus, reason: 'E2E setup' });

  // A task Priya delivered today with approved regular hours, so a bug reported on it is inside warranty.
  const reworkTaskTitle = `E2E saved cards ${stamp}`;
  const rework = await create(reworkTaskTitle);
  await move(rework.id, 'InProgress');
  const log = await call<{ id: number }>(priya.api, 'post', '/api/v1/timelogs', {
    taskId: rework.id,
    workDate: today,
    hours: 6,
    workType: 'Regular',
    bugId: null,
    note: 'E2E delivery work',
  });
  await call(lead.api, 'post', `/api/v1/timelogs/${log.id}/approve`);
  await move(rework.id, 'Completed');

  // A freshly assigned task for the keyboard-only board check.
  const keyboardTaskTitle = `E2E keyboard ${stamp}`;
  const keyboard = await create(keyboardTaskTitle);

  const state: E2eState = { reworkTaskId: rework.id, reworkTaskTitle, keyboardTaskId: keyboard.id, keyboardTaskTitle, month: today.slice(0, 7) };
  mkdirSync(dirname(statePath), { recursive: true });
  writeFileSync(statePath, JSON.stringify(state, null, 2));
  await lead.api.dispose();
  await priya.api.dispose();
}
