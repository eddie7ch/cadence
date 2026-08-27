import { clearSession, getAccessToken } from './session';
import type {
  ActivityDetailDto,
  ActivitySummaryDto,
  AuthResponseDto,
  CoachingReportDto,
  NearbyActivityDto,
  PagedDto,
  ProblemDetails,
  Sport,
  TimeSeriesDto,
  TrendsDto,
} from './types';

const DEFAULT_BASE_URL = 'http://localhost:8080/api/v1';

function resolveBaseUrl(): string {
  const configured = import.meta.env.VITE_API_URL;
  const value = configured !== undefined && configured.trim() !== '' ? configured.trim() : DEFAULT_BASE_URL;
  return value.replace(/\/+$/, '');
}

export const apiBaseUrl = resolveBaseUrl();

/**
 * Every failed request surfaces as this type, whether the API answered with
 * ProblemDetails, answered with something else entirely, or never answered at
 * all (status 0). Screens can therefore render `error.message` unconditionally.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly detail: string | null;
  readonly problem: ProblemDetails | null;

  constructor(status: number, message: string, detail: string | null, problem: ProblemDetails | null) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.detail = detail;
    this.problem = problem;
  }

  /** True when the API is unreachable rather than refusing the request. */
  get isNetworkFailure(): boolean {
    return this.status === 0;
  }

  get isNotFound(): boolean {
    return this.status === 404;
  }

  /** 503 is how the contract reports an unconfigured optional dependency. */
  get isUnavailable(): boolean {
    return this.status === 503;
  }
}

export type QueryValue = string | number | boolean | null | undefined;

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  formData?: FormData;
  query?: Record<string, QueryValue>;
  signal?: AbortSignal;
  /** Skips the Authorization header; only the auth endpoints need this. */
  anonymous?: boolean;
}

function buildUrl(path: string, query?: Record<string, QueryValue>): string {
  const url = `${apiBaseUrl}${path.startsWith('/') ? path : `/${path}`}`;
  if (query === undefined) {
    return url;
  }

  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') {
      continue;
    }

    params.append(key, String(value));
  }

  const serialised = params.toString();
  return serialised === '' ? url : `${url}?${serialised}`;
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return typeof value === 'object' && value !== null;
}

function flattenValidationErrors(errors: Record<string, string[]>): string | null {
  const messages = Object.values(errors).flat();
  return messages.length === 0 ? null : messages.join(' ');
}

async function readProblem(response: Response): Promise<ProblemDetails | null> {
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes('json')) {
    return null;
  }

  try {
    const parsed: unknown = await response.json();
    return isProblemDetails(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

function statusFallbackMessage(status: number): string {
  switch (status) {
    case 400:
      return 'The request was rejected as invalid.';
    case 401:
      return 'Your session has expired. Sign in again.';
    case 403:
      return 'You do not have access to that.';
    case 404:
      return 'Not found.';
    case 409:
      return 'That conflicts with something that already exists.';
    case 422:
      return 'That file or request could not be processed.';
    case 503:
      return 'That service is not available.';
    default:
      return `The request failed (HTTP ${String(status)}).`;
  }
}

async function toApiError(response: Response): Promise<ApiError> {
  const problem = await readProblem(response);
  const validation = problem?.errors !== undefined ? flattenValidationErrors(problem.errors) : null;
  const detail = problem?.detail ?? validation;
  const title = problem?.title;
  const message =
    detail !== null && detail !== undefined && detail !== ''
      ? detail
      : title !== undefined && title !== ''
        ? title
        : statusFallbackMessage(response.status);

  return new ApiError(response.status, message, detail ?? null, problem);
}

async function send(path: string, options: RequestOptions): Promise<Response> {
  const headers = new Headers({ Accept: 'application/json' });

  if (options.anonymous !== true) {
    const token = getAccessToken();
    if (token !== null) {
      headers.set('Authorization', `Bearer ${token}`);
    }
  }

  let body: BodyInit | undefined;
  if (options.formData !== undefined) {
    // Content-Type is deliberately unset so the browser adds the multipart boundary.
    body = options.formData;
  } else if (options.body !== undefined) {
    headers.set('Content-Type', 'application/json');
    body = JSON.stringify(options.body);
  }

  const init: RequestInit = {
    method: options.method ?? 'GET',
    headers,
  };

  if (body !== undefined) {
    init.body = body;
  }

  if (options.signal !== undefined) {
    init.signal = options.signal;
  }

  try {
    return await fetch(buildUrl(path, options.query), init);
  } catch (cause) {
    // An aborted request is a caller decision, not a transport failure, and must
    // stay an AbortError so effects can ignore it.
    if (cause instanceof DOMException && cause.name === 'AbortError') {
      throw cause;
    }

    throw new ApiError(0, `Cannot reach the Cadence API at ${apiBaseUrl}.`, null, null);
  }
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const response = await send(path, options);

  if (!response.ok) {
    const error = await toApiError(response);
    if (response.status === 401 && options.anonymous !== true) {
      // The token is gone or expired; drop it so the app falls back to sign-in
      // rather than looping on requests that can never succeed.
      clearSession();
    }

    throw error;
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export interface ActivityListParams {
  sport?: Sport;
  from?: string;
  to?: string;
  minimumDistanceMeters?: number;
  page?: number;
  pageSize?: number;
}

export interface RegisterPayload {
  email: string;
  password: string;
  displayName: string;
  maxHeartRate?: number;
  restingHeartRate?: number;
}

export interface SignInPayload {
  email: string;
  password: string;
}

/**
 * The whole HTTP surface the app uses, in one place: paths and query-parameter
 * names live here and nowhere else.
 */
export const api = {
  auth: {
    register: (payload: RegisterPayload, signal?: AbortSignal): Promise<AuthResponseDto> =>
      request<AuthResponseDto>('/auth/register', {
        method: 'POST',
        body: payload,
        anonymous: true,
        ...(signal !== undefined ? { signal } : {}),
      }),

    signIn: (payload: SignInPayload, signal?: AbortSignal): Promise<AuthResponseDto> =>
      request<AuthResponseDto>('/auth/login', {
        method: 'POST',
        body: payload,
        anonymous: true,
        ...(signal !== undefined ? { signal } : {}),
      }),
  },

  activities: {
    list: (params: ActivityListParams, signal?: AbortSignal): Promise<PagedDto<ActivitySummaryDto>> =>
      request<PagedDto<ActivitySummaryDto>>('/activities', {
        query: {
          sport: params.sport,
          from: params.from,
          to: params.to,
          minimumDistanceMeters: params.minimumDistanceMeters,
          page: params.page,
          pageSize: params.pageSize,
        },
        ...(signal !== undefined ? { signal } : {}),
      }),

    detail: (id: string, signal?: AbortSignal): Promise<ActivityDetailDto> =>
      request<ActivityDetailDto>(`/activities/${encodeURIComponent(id)}`, {
        ...(signal !== undefined ? { signal } : {}),
      }),

    /**
     * `maxPoints` caps the response so a three-hour ride does not ship 40,000
     * samples to a chart that can only draw a few hundred pixels wide.
     */
    series: (id: string, maxPoints: number, signal?: AbortSignal): Promise<TimeSeriesDto> =>
      request<TimeSeriesDto>(`/activities/${encodeURIComponent(id)}/series`, {
        query: { maxPoints },
        ...(signal !== undefined ? { signal } : {}),
      }),

    nearby: (
      latitude: number,
      longitude: number,
      radiusMeters: number,
      signal?: AbortSignal,
    ): Promise<NearbyActivityDto[]> =>
      request<NearbyActivityDto[]>('/activities/nearby', {
        query: { latitude, longitude, radiusMeters },
        ...(signal !== undefined ? { signal } : {}),
      }),

    upload: (file: File, signal?: AbortSignal): Promise<ActivitySummaryDto> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return request<ActivitySummaryDto>('/activities', {
        method: 'POST',
        formData: form,
        ...(signal !== undefined ? { signal } : {}),
      });
    },
  },

  trends: {
    get: (weeks: number, signal?: AbortSignal): Promise<TrendsDto> =>
      request<TrendsDto>('/trends', {
        query: { weeks },
        ...(signal !== undefined ? { signal } : {}),
      }),
  },

  coaching: {
    /** Resolves to null when the athlete has never generated a report. */
    latest: async (signal?: AbortSignal): Promise<CoachingReportDto | null> => {
      try {
        return await request<CoachingReportDto>('/coaching/reports/latest', {
          ...(signal !== undefined ? { signal } : {}),
        });
      } catch (error) {
        if (error instanceof ApiError && error.isNotFound) {
          return null;
        }

        throw error;
      }
    },

    generate: (signal?: AbortSignal): Promise<CoachingReportDto> =>
      request<CoachingReportDto>('/coaching/reports', {
        method: 'POST',
        ...(signal !== undefined ? { signal } : {}),
      }),
  },
};

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return error.message;
  }

  if (error instanceof Error) {
    return error.message;
  }

  return 'Something went wrong.';
}

export function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}
