import axios from 'axios';
import type {
  AxiosRequestConfig,
  AxiosResponse,
  InternalAxiosRequestConfig,
} from 'axios';
import { useAuthStore } from '../stores/auth';
import { ResultStatus } from '../types/api';
import type { ApiResult } from '../types/api';

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

const handleHttpError = (error: unknown) => {
  if (axios.isAxiosError(error) && error.response) {
    if (error.response.status === 401) {
      logoutAndRedirect();
    } else if (error.response.status === 403) {
      alert('当前账号无权访问该功能。');
    } else if (error.response.status === 400) {
      alert('请求参数校验失败。');
    } else if (error.response.status === 500) {
      alert('服务端发生异常，请稍后重试。');
    }
  } else {
    alert('网络异常，请检查后端服务是否正常。');
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
        alert(`提示：\n${errorMessage}`);
        return Promise.reject(apiResult);
      }

      case ResultStatus.Forbidden:
        console.warn('收到无权访问响应。');
        alert('当前账号无权执行该操作。');
        return Promise.reject(apiResult);

      case ResultStatus.Unauthorized:
        console.warn('登录状态已过期。');
        logoutAndRedirect();
        return Promise.reject(apiResult);

      default:
        return Promise.reject(apiResult);
    }
  } catch (error) {
    return handleHttpError(error);
  }
};

const http = {
  get<T>(url: string, config?: AxiosRequestConfig) {
    return unwrap<T>(client.get<ApiResult<T> | T>(url, config));
  },
  post<T>(url: string, data?: unknown, config?: AxiosRequestConfig) {
    return unwrap<T>(client.post<ApiResult<T> | T>(url, data, config));
  },
  put<T>(url: string, data?: unknown, config?: AxiosRequestConfig) {
    return unwrap<T>(client.put<ApiResult<T> | T>(url, data, config));
  },
  delete<T>(url: string, config?: AxiosRequestConfig) {
    return unwrap<T>(client.delete<ApiResult<T> | T>(url, config));
  },
};

export default http;
