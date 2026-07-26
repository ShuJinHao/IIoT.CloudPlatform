import axios from 'axios';
import { resolveApiResultNotification } from '../../core/http/apiResult';
import { readProblemDetails, resolveProblemNotification } from '../../core/http/problemDetails';
import { isApiResult } from '../../core/types/api';

export async function resolveDashboardLoadError(error: unknown): Promise<string> {
  if (axios.isAxiosError(error) && error.response) {
    const contentType = error.response.headers?.['content-type'] as string | undefined;
    const problem = await readProblemDetails(error.response.data, contentType);
    return resolveProblemNotification(error.response.status, problem).message;
  }

  if (isApiResult(error)) {
    return resolveApiResultNotification(error).message;
  }

  return '网络请求失败，请检查服务状态后重试。';
}
