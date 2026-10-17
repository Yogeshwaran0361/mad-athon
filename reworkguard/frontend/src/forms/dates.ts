// Difference between the API's clock and the browser's, learned from the X-Server-Time header. The API validates dates
// against its own clock, which a development clock control can move ahead of the browser's.
let serverOffsetMs = 0;

/** Records the API clock from an X-Server-Time header value; ignores missing or unparsable values. */
export function noteServerTime(header: string | null): void {
  const serverMs = header ? Date.parse(header) : NaN;
  if (!Number.isNaN(serverMs)) {
    serverOffsetMs = serverMs - Date.now();
  }
}

/** Today's date as yyyy-MM-dd in UTC by the API's clock, matching how the API stores dates. */
export function todayUtc(): string {
  return new Date(Date.now() + serverOffsetMs).toISOString().slice(0, 10);
}

/** The current month as yyyy-MM in UTC. */
export function currentMonthUtc(): string {
  return todayUtc().slice(0, 7);
}
