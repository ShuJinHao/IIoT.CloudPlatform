import axios from 'axios';
import type {
  AxiosRequestConfig,
  AxiosResponse,
  InternalAxiosRequestConfig,
} from 'axios';
import { useAuthStore } from '../../stores/auth';
import { notifyError } from '../../utils/feedback';
import { isApiResult, ResultStatus, type ApiResult } from '../types/api';
import { resolveApiResultNotification } from './apiResult';
import { readProblemDetails, resolveProblemNotification } from './problemDetails';

// 统一反馈路由选项：调用方声明由自己内联呈现错误时，httpClient 仍走中央
// ProblemDetails 解析与 reject，只是不再额外弹全局错误通知，避免双层弹窗。
export interface HttpFeedbackOptions {
  /** true 表示错误由调用方内联展示，不再触发全局错误通知。 */
  inlineFeedback?: boolean;
}

type HttpRequestConfig = AxiosRequestConfig & HttpFeedbackOptions;

const client = axios.create({
  baseURL: '/api/v1',
  timeout: 15000,
  headers: {
    'Content-Type': 'application/json',
  },
});

client.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const token = localStorage.getItem('token');

    if (token && config.headers) {
      config.headers.Authorization = `Bearer ${token}`;
    }

    return config;
  },
  (error) => Promise.reject(error),
);

function logoutAndRedirect() {
  const authStore = useAuthStore();
  authStore.logout({ redirectToLogin: true });
}

async function notifyProblem(status: number, data: unknown, contentType?: string) {
  const problem = await readProblemDetails(data, contentType);
  const notification = resolveProblemNotification(status, problem);
  notifyError(notification.message, {
    title: notification.title,
    details: notification.details,
  });
}

const handleHttpError = async (error: unknown, inlineFeedback = false): Promise<never> => {
  if (axios.isAxiosError(error) && error.response) {
    if (error.response.status === 401) {
      logoutAndRedirect();
    } else if (!inlineFeedback) {
      const contentType = error.response.headers?.['content-type'] as string | undefined;
      await notifyProblem(error.response.status, error.response.data, contentType);
    }
  } else if (!inlineFeedback) {
    notifyError('网络异常，请检查后端服务是否正常。', {
      title: '网络请求失败',
    });
  }

  return Promise.reject(error);
};

const unwrap = async <T>(
  request: Promise<AxiosResponse<ApiResult<T> | T>>,
  inlineFeedback = false,
): Promise<T> => {
  try {
    const response = await request;
    const result = response.data;

    if (!isApiResult<T>(result)) {
      return result as T;
    }

    switch (result.status) {
      case ResultStatus.Ok:
        return result.value as T;

      case ResultStatus.Unauthorized:
        logoutAndRedirect();
        return Promise.reject(result);

      case ResultStatus.Error:
      case ResultStatus.Invalid:
      case ResultStatus.NotFound:
      case ResultStatus.Forbidden: {
        if (!inlineFeedback) {
          const notification = resolveApiResultNotification(result);
          notifyError(notification.message, { title: notification.title });
        }
        return Promise.reject(result);
      }

      default:
        return Promise.reject(result);
    }
  } catch (error) {
    return await handleHttpError(error, inlineFeedback);
  }
};

const http = {
  get<T>(url: string, config?: HttpRequestConfig) {
    return unwrap<T>(client.get<ApiResult<T> | T>(url, config), config?.inlineFeedback);
  },
  post<T>(url: string, data?: unknown, config?: HttpRequestConfig) {
    return unwrap<T>(client.post<ApiResult<T> | T>(url, data, config), config?.inlineFeedback);
  },
  postRaw<T>(url: string, data?: unknown, config?: HttpRequestConfig) {
    return client.post<T>(url, data, config).catch((error) => handleHttpError(error, config?.inlineFeedback));
  },
  put<T>(url: string, data?: unknown, config?: HttpRequestConfig) {
    return unwrap<T>(client.put<ApiResult<T> | T>(url, data, config), config?.inlineFeedback);
  },
  delete<T>(url: string, config?: HttpRequestConfig) {
    return unwrap<T>(client.delete<ApiResult<T> | T>(url, config), config?.inlineFeedback);
  },
};

export type HttpClient = typeof http;
export default http;
