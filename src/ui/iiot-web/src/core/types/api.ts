export const ResultStatus = {
  Ok: 0,
  Error: 1,
  Forbidden: 2,
  Unauthorized: 3,
  NotFound: 4,
  Invalid: 5,
} as const;

export type ResultStatusType = typeof ResultStatus[keyof typeof ResultStatus];

export interface ApiResult<T = unknown> {
  isSuccess: boolean;
  status: ResultStatusType;
  value?: T;
  errors?: unknown[];
}

export function isApiResult<T = unknown>(value: unknown): value is ApiResult<T> {
  return typeof value === 'object'
    && value !== null
    && 'status' in value;
}
