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

const handleHttpError = async (error: unknown): Promise<never> => {
  if (axios.isAxiosError(error) && error.response) {
    const contentType = error.response.headers?.['content-type'] as string | undefined;
    if (error.response.status === 401) {
      logoutAndRedirect();
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
        const notification = resolveApiResultNotification(result);
        notifyError(notification.message, { title: notification.title });
        return Promise.reject(result);
      }

      default:
        return Promise.reject(result);
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

export type HttpClient = typeof http;
export default http;
