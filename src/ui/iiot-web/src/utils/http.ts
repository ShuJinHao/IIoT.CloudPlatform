import axios from 'axios';
import type {
  AxiosRequestConfig,
  AxiosResponse,
  InternalAxiosRequestConfig,
} from 'axios';
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

const clearStoredSession = () => {
  localStorage.removeItem('token');
  localStorage.removeItem('refreshToken');
  localStorage.removeItem('accessTokenExpiresAt');
  localStorage.removeItem('refreshTokenExpiresAt');
};

const handleHttpError = (error: unknown) => {
  if (axios.isAxiosError(error) && error.response) {
    if (error.response.status === 401) {
      clearStoredSession();
      window.location.href = '/login';
    } else if (error.response.status === 403) {
      alert('系统拒绝访问：权限不足 (HTTP 403)');
    } else if (error.response.status === 400) {
      alert('请求参数格式有误 (HTTP 400)');
    } else if (error.response.status === 500) {
      alert('服务器内部错误，请联系管理员查看日志。');
    }
  } else {
    alert('网络连接异常，请检查后端服务是否正常启动。');
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
        const errorMessage = apiResult.errors?.join('\n') || '业务处理失败';
        console.error('业务拦截:', errorMessage);
        alert(`提示:\n${errorMessage}`);
        return Promise.reject(apiResult);
      }

      case ResultStatus.Forbidden:
        console.warn('越权警告：您没有权限执行此操作。');
        alert('越权警告：您没有该模块或数据的管理权限。');
        return Promise.reject(apiResult);

      case ResultStatus.Unauthorized:
        console.warn('凭证已过期。');
        clearStoredSession();
        alert('登录已过期，请重新登录。');
        window.location.href = '/login';
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
