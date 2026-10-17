import { noteServerTime } from '../forms/dates';

const TOKEN_KEY = 'reworkguard.token';
const SERVER_TIME_HEADER = 'X-Server-Time';

/** RFC 7807 problem returned by the API, with optional validation errors and a domain error code. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string | undefined;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, message: string, code?: string, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.fieldErrors = fieldErrors;
  }
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

export const tokenStore = {
  get: (): string | null => sessionStorage.getItem(TOKEN_KEY),
  set: (token: string) => {
    sessionStorage.setItem(TOKEN_KEY, token);
  },
  clear: () => {
    sessionStorage.removeItem(TOKEN_KEY);
  },
};

let onUnauthorized: () => void = () => undefined;
let refreshing: Promise<boolean> | null = null;

/**
 * Exchanges the HttpOnly refresh cookie for a new access token. Concurrent callers share one request, because the
 * server rotates the cookie on every use and treats a second use of the old one as theft.
 */
export function refreshAccessToken(): Promise<boolean> {
  refreshing ??= fetch('/api/v1/auth/refresh', { method: 'POST', credentials: 'same-origin', headers: { Accept: 'application/json' } })
    .then(async (response) => {
      if (!response.ok) {
        return false;
      }
      const body = (await response.json()) as { accessToken: string };
      tokenStore.set(body.accessToken);
      return true;
    })
    .catch(() => false)
    .finally(() => {
      refreshing = null;
    });
  return refreshing;
}

/** Sends a request; on a 401 with a session, refreshes the access token once and retries. */
async function send(path: string, init: RequestInit, accept: string): Promise<Response> {
  const hadToken = tokenStore.get() !== null;
  const attempt = async () => {
    const result = await fetch(`/api/v1${path}`, { ...init, headers: { ...authHeaders(accept), ...(init.headers as Record<string, string> | undefined) } });
    noteServerTime(result.headers.get(SERVER_TIME_HEADER));
    return result;
  };
  let response = await attempt();
  if (response.status === 401 && hadToken && !path.startsWith('/auth/')) {
    if (await refreshAccessToken()) {
      response = await attempt();
    }
    if (response.status === 401) {
      onUnauthorized();
    }
  }
  return response;
}

export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler;
}

/** Builds a query string from defined, non-empty values. */
export function qs(params: Record<string, string | number | boolean | null | undefined>): string {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      query.set(key, String(value));
    }
  }
  const text = query.toString();
  return text ? `?${text}` : '';
}

async function toApiError(response: Response): Promise<ApiError> {
  const problem = (await response.json().catch(() => ({}))) as ProblemDetails;
  return new ApiError(
    response.status,
    problem.detail ?? problem.title ?? `Request failed (${response.status})`,
    problem.code,
    problem.errors ?? {},
  );
}

function authHeaders(accept: string): Record<string, string> {
  const headers: Record<string, string> = { Accept: accept };
  const token = tokenStore.get();
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }
  return headers;
}

/** Fetches a file with the bearer token and returns it with the server-suggested file name. */
export async function apiBlob(path: string, fallbackName: string): Promise<{ blob: Blob; fileName: string }> {
  const response = await send(path, {}, '*/*');
  if (!response.ok) {
    throw await toApiError(response);
  }
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const fileName = match?.[1] ? decodeURIComponent(match[1]) : fallbackName;
  return { blob: await response.blob(), fileName };
}

/** Downloads a file through an authorized fetch and saves it via a temporary object URL. */
export async function downloadFile(path: string, fallbackName: string): Promise<void> {
  const { blob, fileName } = await apiBlob(path, fallbackName);
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => {
    URL.revokeObjectURL(url);
  }, 1000);
}

export async function api<T>(path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  const response = await send(
    path,
    {
      method: init.method ?? 'GET',
      headers: init.body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: init.body === undefined ? undefined : JSON.stringify(init.body),
    },
    'application/json',
  );

  if (!response.ok) {
    throw await toApiError(response);
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}
