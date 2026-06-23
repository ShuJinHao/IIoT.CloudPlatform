import axios, { type AxiosResponse } from 'axios';

export interface LoginPayload {
  employeeNo: string;
  password?: string;
}

export interface AuthSessionPayload {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

export type AuthRequestErrorKind =
  | 'invalid-credentials'
  | 'session-invalid'
  | 'network'
  | 'timeout'
  | 'rate-limited'
  | 'server'
  | 'invalid-response'
  | 'unknown';

interface ProblemDetailsPayload {
  title?: string;
  detail?: string;
  errors?: unknown;
  [key: string]: unknown;
}

export class AuthRequestError extends Error {
  public readonly kind: AuthRequestErrorKind;
  public readonly status?: number;
  public readonly detail?: string;

  constructor(
    kind: AuthRequestErrorKind,
    message: string,
    status?: number,
    detail?: string,
  ) {
    super(message);
    this.name = 'AuthRequestError';
    this.kind = kind;
    this.status = status;
    this.detail = detail;
  }
}

const authClient = axios.create({
  baseURL: '/api/v1',
  timeout: 15000,
  headers: {
    'Content-Type': 'application/json',
  },
});

const basePath = '/human/identity';
const refreshTokenHeader = 'x-iiot-refresh-token';
const refreshTokenExpiresAtHeader = 'x-iiot-refresh-token-expires-at';
const accessTokenExpiresAtHeader = 'x-iiot-access-token-expires-at';

const normalizeErrors = (errors: unknown): string[] => {
  if (!errors) return [];
  if (Array.isArray(errors)) {
    return errors.map((item) => String(item).trim()).filter(Boolean);
  }
  if (typeof errors === 'object') {
    return Object.values(errors as Record<string, unknown>)
      .flatMap((value) => Array.isArray(value) ? value : [value])
      .map((item) => String(item).trim())
      .filter(Boolean);
  }
  return [String(errors).trim()].filter(Boolean);
};

const readProblemMessage = (data: unknown): string | undefined => {
  if (!data) return undefined;

  if (typeof data === 'string') {
    const trimmed = data.trim();
    if (!trimmed) return undefined;

    try {
      return readProblemMessage(JSON.parse(trimmed));
    } catch {
      return trimmed;
    }
  }

  if (typeof data !== 'object') {
    return String(data).trim() || undefined;
  }

  const problem = data as ProblemDetailsPayload;
  const details = [
    ...normalizeErrors(problem.errors),
    ...normalizeErrors(problem.extensions && (problem.extensions as Record<string, unknown>).errors),
  ];

  return problem.detail?.trim()
    || details[0]
    || problem.title?.trim()
    || undefined;
};

const toAuthRequestError = (
  error: unknown,
  context: 'login' | 'refresh',
): AuthRequestError => {
  if (error instanceof AuthRequestError) {
    return error;
  }

  if (!axios.isAxiosError(error)) {
    return new AuthRequestError(
      'unknown',
      error instanceof Error ? error.message : 'Authentication request failed.',
    );
  }

  if (!error.response) {
    const timedOut =
      error.code === 'ECONNABORTED' ||
      error.code === 'ETIMEDOUT' ||
      error.message.toLowerCase().includes('timeout');

    return new AuthRequestError(
      timedOut ? 'timeout' : 'network',
      timedOut ? 'Authentication request timed out.' : 'Authentication service is unreachable.',
    );
  }

  const status = error.response.status;
  const detail = readProblemMessage(error.response.data);

  if (context === 'login' && (status === 400 || status === 401)) {
    return new AuthRequestError(
      'invalid-credentials',
      detail || 'Invalid employee number or password.',
      status,
      detail,
    );
  }

  if (status === 401) {
    return new AuthRequestError(
      'session-invalid',
      detail || 'Authentication session is invalid or expired.',
      status,
      detail,
    );
  }

  if (status === 429) {
    return new AuthRequestError(
      'rate-limited',
      detail || 'Authentication request was rate limited.',
      status,
      detail,
    );
  }

  if (status >= 500) {
    return new AuthRequestError(
      'server',
      detail || 'Authentication service failed.',
      status,
      detail,
    );
  }

  return new AuthRequestError(
    'unknown',
    detail || 'Authentication request failed.',
    status,
    detail,
  );
};

function parseSession(response: AxiosResponse<string>): AuthSessionPayload {
  const accessToken = typeof response.data === 'string' ? response.data : '';
  const refreshToken = response.headers[refreshTokenHeader];
  const refreshTokenExpiresAt = response.headers[refreshTokenExpiresAtHeader];
  const accessTokenExpiresAt = response.headers[accessTokenExpiresAtHeader];

  if (!accessToken || !refreshToken || !refreshTokenExpiresAt || !accessTokenExpiresAt) {
    throw new AuthRequestError(
      'invalid-response',
      'Authentication response is missing session headers.',
    );
  }

  return {
    accessToken,
    refreshToken,
    refreshTokenExpiresAt,
    accessTokenExpiresAt,
  };
}

export const loginApi = async (data: LoginPayload): Promise<AuthSessionPayload> => {
  try {
    const response = await authClient.post<string>(`${basePath}/login`, {
      employeeNo: data.employeeNo,
      password: data.password,
    });

    return parseSession(response);
  } catch (error) {
    throw toAuthRequestError(error, 'login');
  }
};

export const refreshHumanSessionApi = async (refreshToken: string): Promise<AuthSessionPayload> => {
  try {
    const response = await authClient.post<string>(
      `${basePath}/refresh`,
      null,
      {
        headers: {
          'X-IIoT-Refresh-Token': refreshToken,
        },
      },
    );

    return parseSession(response);
  } catch (error) {
    throw toAuthRequestError(error, 'refresh');
  }
};

export const isSessionInvalidAuthError = (error: unknown): boolean => {
  return error instanceof AuthRequestError && error.kind === 'session-invalid';
};

export const isTransientAuthError = (error: unknown): boolean => {
  return error instanceof AuthRequestError &&
    (
      error.kind === 'network' ||
      error.kind === 'timeout' ||
      error.kind === 'rate-limited' ||
      error.kind === 'server' ||
      error.kind === 'invalid-response'
    );
};
