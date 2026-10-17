import { afterEach, describe, expect, it, vi } from 'vitest';
import { noteServerTime, todayUtc } from './dates';

describe('todayUtc', () => {
  afterEach(() => {
    noteServerTime(new Date().toISOString());
    vi.useRealTimers();
  });

  it('follows the API clock once a server time has been seen', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-17T03:00:00Z'));
    noteServerTime('2026-10-22T09:00:00Z');
    expect(todayUtc()).toBe('2026-10-22');
  });

  it('ignores a missing or invalid header', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-17T03:00:00Z'));
    noteServerTime('2026-10-17T03:00:00Z');
    noteServerTime(null);
    noteServerTime('not a date');
    expect(todayUtc()).toBe('2026-10-17');
  });
});
