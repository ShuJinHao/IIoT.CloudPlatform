import axios from 'axios';
import { isApiResult } from '../types/api';
import { resolveApiResultNotification } from './apiResult';
import { readProblemDetails, resolveProblemNotification } from './problemDetails';

export async function resolveRequestErrorMessage(
  error: unknown,
  fallback: string,
): Promise<string> {
  if (axios.isAxiosError(error) && error.response) {
    const contentType = error.response.headers?.['content-type'] as string | undefined;
    const problem = await readProblemDetails(error.response.data, contentType);
    return resolveProblemNotification(error.response.status, problem).message;
  }

  if (isApiResult(error)) {
    return resolveApiResultNotification(error).message;
  }

  return fallback;
}
