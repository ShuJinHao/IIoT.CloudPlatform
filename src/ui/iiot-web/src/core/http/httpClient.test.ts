import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axios from 'axios';

// 走真实 httpClient：只把 axios 适配层换成可控桩，HTTP 状态码与 ProblemDetails
// 解析、全局通知路由、inlineFeedback 选项都是真实代码路径。用 spyOn 监听真实
// feedback 模块的 notifyError，验证只有一个错误面。

function axiosError(status: number, data: unknown) {
  const err = new axios.AxiosError(`Request failed with status code ${status}`);
  err.response = {
    status,
    data,
    headers: {},
    config: {} as never,
    statusText: String(status),
  };
  return err;
}

type Responder = (config: { url?: string; method?: string }) => Promise<never>;

describe('httpClient 统一反馈路由（真实错误链）', () => {
  let notifyErrorSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    // 不 resetModules，保证 httpClient 与测试共享同一个 feedback reactive state。
    const feedback = await import('../../utils/feedback');
    notifyErrorSpy = vi.spyOn(feedback, 'notifyError').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.resetModules();
  });

  async function loadClient(responder: Responder) {
    vi.resetModules();
    // 重新绑定 spy 到新模块实例前先建立 axios 适配桩。
    vi.doMock('axios', async () => {
      const actual = await vi.importActual<typeof import('axios')>('axios');
      const adapter = (config: { url?: string; method?: string }) => responder(config);
      const withAdapter = (cfg?: unknown) => ({ ...(cfg as object), adapter } as never);
      return {
        ...actual,
        default: {
          ...actual.default,
          create: () => ({
            interceptors: {
              request: { use: vi.fn() },
              response: { use: vi.fn() },
            },
            get: (url: string, config?: unknown) => actual.default.get(url, withAdapter(config)),
            post: (url: string, data?: unknown, config?: unknown) => actual.default.post(url, data, withAdapter(config)),
            put: (url: string, data?: unknown, config?: unknown) => actual.default.put(url, data, withAdapter(config)),
            delete: (url: string, config?: unknown) => actual.default.delete(url, withAdapter(config)),
          }),
        },
      };
    });
    // 重新绑定 notifyError spy 到本次 import 的 feedback 实例。
    const feedback = await import('../../utils/feedback');
    notifyErrorSpy = vi.spyOn(feedback, 'notifyError').mockImplementation(() => {});
    return (await import('./httpClient')).default;
  }

  it('默认模式下 HTTP 400 弹全局错误通知并 reject', async () => {
    const problem = { title: 'Bad Request', status: 400, detail: '清理失败', errors: ['文件被占用'] };
    const http = await loadClient(() => Promise.reject(axiosError(400, problem)));

    await expect(
      http.delete('/human/client-releases/components/x', { data: { reason: 'r' } }),
    ).rejects.toBeTruthy();
    expect(notifyErrorSpy).toHaveBeenCalledTimes(1);
    expect(notifyErrorSpy.mock.calls[0]![0]).toContain('清理失败');
  });

  it('inlineFeedback 模式下 HTTP 400 不弹全局通知，只 reject 给调用方内联处理', async () => {
    const problem = {
      title: 'Bad Request',
      status: 400,
      detail: '组件元数据已删除，但发布文件清理失败，已登记恢复操作 9d4e5f6a-7777-4888-8999-aaaabbbbcccc',
      errors: ['edge-updates/host/2.4.0/x.exe 文件被进程占用'],
    };
    const http = await loadClient(() => Promise.reject(axiosError(400, problem)));

    await expect(
      http.delete('/human/client-releases/components/x', { data: { reason: 'r' }, inlineFeedback: true }),
    ).rejects.toBeTruthy();
    // 唯一错误面是调用方内联：全局错误通知不应被触发。
    expect(notifyErrorSpy).not.toHaveBeenCalled();
  });

  it('inlineFeedback 模式下网络异常也不弹全局通知', async () => {
    const http = await loadClient(() => Promise.reject(new Error('Network Error')));

    await expect(http.get('/human/client-releases/catalog', { inlineFeedback: true })).rejects.toBeTruthy();
    expect(notifyErrorSpy).not.toHaveBeenCalled();
  });
});
