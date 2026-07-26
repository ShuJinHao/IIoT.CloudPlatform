import axios from 'axios';
import { resolveApiResultNotification } from '../../core/http/apiResult';
import { readProblemDetails, resolveProblemNotification } from '../../core/http/problemDetails';
import { isApiResult } from '../../core/types/api';
import { CapacityPayloadError } from './types';

export interface CapacityLoadError {
  kind: 'api' | 'payload';
  title: string;
  message: string;
}

export async function resolveCapacityLoadError(error: unknown): Promise<CapacityLoadError> {
  if (error instanceof CapacityPayloadError) {
    return {
      kind: 'payload',
      title: '产能数据解析失败',
      message: `${error.message} 请重试；若持续出现，请检查接口或缓存数据。`,
    };
  }

  if (axios.isAxiosError(error) && error.response) {
    const contentType = error.response.headers?.['content-type'] as string | undefined;
    const problem = await readProblemDetails(error.response.data, contentType);
    const notification = resolveProblemNotification(error.response.status, problem);
    return {
      kind: 'api',
      title: '产能数据加载失败',
      message: notification.message,
    };
  }

  if (isApiResult(error)) {
    const notification = resolveApiResultNotification(error);
    return {
      kind: 'api',
      title: notification.title,
      message: notification.message,
    };
  }

  return {
    kind: 'api',
    title: '产能数据加载失败',
    message: error instanceof Error && error.message.trim()
      ? error.message
      : '网络请求失败，请检查服务状态后重试。',
  };
}
