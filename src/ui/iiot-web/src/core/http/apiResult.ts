import { ResultStatus, type ApiResult } from '../types/api';
import { normalizeErrors } from './problemDetails';

export interface ApiResultNotification {
  title: string;
  message: string;
}

export function resolveApiResultNotification(result: ApiResult): ApiResultNotification {
  const errors = normalizeErrors(result.errors);
  const message = errors.join('\n') || '业务请求失败。';

  if (result.status === ResultStatus.Forbidden) {
    return {
      title: '禁止访问',
      message: errors[0] || '当前账号无权执行该操作。',
    };
  }

  return {
    title: '请求处理失败',
    message,
  };
}
