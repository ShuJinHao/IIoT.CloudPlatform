import axios from 'axios';
import type {
  AxiosRequestConfig,
  AxiosResponse,
  InternalAxiosRequestConfig,
} from 'axios';
import { useAuthStore } from '../stores/auth';
import { ResultStatus } from '../types/api';
import type { ApiResult } from '../types/api';
import { notifyError } from './feedback';

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

interface ProblemDetailsPayload {
  title?: string;
  detail?: string;
  errors?: unknown;
  [key: string]: unknown;
}

const statusFallbackMessages: Record<number, string> = {
  400: '请求无效，服务器没有返回具体原因。',
  403: '当前账号无权访问该功能。',
  404: '请求的资源不存在或已被删除。',
  500: '服务端发生异常，请稍后重试。',
};

const normalizeErrors = (errors: unknown): string[] => {
  if (!errors) return [];
  if (Array.isArray(errors)) {
    return errors
      .map((item) => String(item).trim())
      .filter(Boolean);
  }
  if (typeof errors === 'object') {
    return Object.values(errors as Record<string, unknown>)
      .flatMap((value) => Array.isArray(value) ? value : [value])
      .map((item) => String(item).trim())
      .filter(Boolean);
  }
  return [String(errors).trim()].filter(Boolean);
};

const readProblemDetails = async (data: unknown, contentType?: string): Promise<ProblemDetailsPayload | null> => {
  if (!data) return null;

  if (data instanceof Blob) {
    const text = await data.text();
    if (!text.trim()) return null;
    if (contentType?.includes('json') || text.trimStart().startsWith('{')) {
      try {
        return JSON.parse(text) as ProblemDetailsPayload;
      } catch {
        return { detail: text };
      }
    }
    return { detail: text };
  }

  if (typeof data === 'object') {
    return data as ProblemDetailsPayload;
  }

  if (typeof data === 'string' && data.trim()) {
    try {
      return JSON.parse(data) as ProblemDetailsPayload;
    } catch {
      return { detail: data };
    }
  }

  return null;
};

const notifyProblem = async (status: number, data: unknown, contentType?: string) => {
  const problem = await readProblemDetails(data, contentType);
  const details = [
    ...normalizeErrors(problem?.errors),
    ...normalizeErrors(problem?.extensions && (problem.extensions as Record<string, unknown>).errors),
  ];
  const message = problem?.detail?.trim()
    || details[0]
    || problem?.title?.trim()
    || statusFallbackMessages[status]
    || '请求失败，请稍后重试。';

  notifyError(message, {
    title: status === 400 ? '请求处理失败' : problem?.title || '请求失败',
    details: details.filter((item) => item !== message),
  });
};

const handleHttpError = async (error: unknown): Promise<never> => {
  if (axios.isAxiosError(error) && error.response) {
    const contentType = error.response.headers?.['content-type'] as string | undefined;
    if (error.response.status === 401) {
      logoutAndRedirect();
    } else if (error.response.status === 403) {
      await notifyProblem(error.response.status, error.response.data, contentType);
    } else if (error.response.status === 400) {
      await notifyProblem(error.response.status, error.response.data, contentType);
    } else if (error.response.status === 500) {
      await notifyProblem(error.response.status, error.response.data, contentType);
    } else {
      await notifyProblem(error.response.status, error.response.data, contentType);
    }
  } else {
    notifyError('网络异常，请检查后端服务是否正常。', {
      title: '网络请求失败',
    });
  }

  return Promise.reject(error);
};

const unwrap = async <T>(request: Promise<AxiosResponse<ApiResult<T> | T>>): Promise<T> => {
  try {
    const response = await request;
    const result = response.data;

    if (typeof result !== 'object' || result === null || !('status' in result)) {
      return result as T;
    }

    const apiResult = result as ApiResult<T>;

    switch (apiResult.status) {
      case ResultStatus.Ok:
        return apiResult.value as T;

      case ResultStatus.Error:
      case ResultStatus.Invalid:
      case ResultStatus.NotFound: {
        const errorMessage = apiResult.errors?.join('\n') || '业务请求失败。';
        console.error('业务拦截：', errorMessage);
        notifyError(errorMessage, { title: '请求处理失败' });
        return Promise.reject(apiResult);
      }

      case ResultStatus.Forbidden:
        console.warn('收到无权访问响应。');
        notifyError('当前账号无权执行该操作。', { title: '禁止访问' });
        return Promise.reject(apiResult);

      case ResultStatus.Unauthorized:
        console.warn('登录状态已过期。');
        logoutAndRedirect();
        return Promise.reject(apiResult);

      default:
        return Promise.reject(apiResult);
    }
  } catch (error) {
    return await handleHttpError(error);
  }
};

const http = {
  get<T>(url: string, config?: AxiosRequestConfig) {
    return unwrap<T>(client.get<ApiResult<T> | T>(url, config));
  },
  post<T>(url: string, data?: unknown, config?: AxiosRequestConfig) {
    return unwrap<T>(client.post<ApiResult<T> | T>(url, data, config));
  },
  postRaw<T>(url: string, data?: unknown, config?: AxiosRequestConfig) {
    return client.post<T>(url, data, config).catch(handleHttpError);
  },
  put<T>(url: string, data?: unknown, config?: AxiosRequestConfig) {
    return unwrap<T>(client.put<ApiResult<T> | T>(url, data, config));
  },
  delete<T>(url: string, config?: AxiosRequestConfig) {
    return unwrap<T>(client.delete<ApiResult<T> | T>(url, config));
  },
};

export default http;
