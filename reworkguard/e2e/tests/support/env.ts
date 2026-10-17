import { readFileSync } from 'node:fs';
import { join } from 'node:path';

export const apiUrl = process.env.E2E_API_URL ?? 'http://127.0.0.1:5080';

/** Seeded demo password for this run; the runner generates it and passes it through the environment. */
export function password(): string {
  const value = process.env.E2E_PASSWORD;
  if (!value) {
    throw new Error('E2E_PASSWORD is not set. Run the suite through `npm run e2e`, or export the seed password of the stack you point it at.');
  }
  return value;
}

/** Seeded demo users (see DbSeeder). */
export const users = {
  admin: 'admin@reworkguard.local',
  manager: 'manager@reworkguard.local',
  lead: 'lead@reworkguard.local',
  qa: 'qa@reworkguard.local',
  finance: 'finance@reworkguard.local',
  priya: 'priya@reworkguard.local',
} as const;

export type UserKey = keyof typeof users;

export interface E2eState {
  reworkTaskId: number;
  reworkTaskTitle: string;
  keyboardTaskId: number;
  keyboardTaskTitle: string;
  month: string;
}

export const statePath = join(import.meta.dirname, '..', '..', '.state', 'data.json');

export function loadState(): E2eState {
  return JSON.parse(readFileSync(statePath, 'utf8')) as E2eState;
}

export function todayUtc(): string {
  return new Date().toISOString().slice(0, 10);
}
