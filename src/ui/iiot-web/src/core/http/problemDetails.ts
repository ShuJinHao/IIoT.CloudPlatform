export interface ProblemDetailsPayload {
  title?: string;
  detail?: string;
  errors?: unknown;
  extensions?: Record<string, unknown>;
  [key: string]: unknown;
}

export interface ProblemNotification {
  title: string;
  message: string;
  details: string[];
}

const statusFallbackMessages: Record<number, string> = {
  400: '请求无效，服务器没有返回具体原因。',
  403: '当前账号无权访问该功能。',
  404: '请求的资源不存在或已被删除。',
  500: '服务端发生异常，请稍后重试。',
};

export function normalizeErrors(errors: unknown): string[] {
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
}

export async function readProblemDetails(
  data: unknown,
  contentType?: string,
): Promise<ProblemDetailsPayload | null> {
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
}

export function resolveProblemNotification(
  status: number,
  problem: ProblemDetailsPayload | null,
): ProblemNotification {
  const details = [
    ...normalizeErrors(problem?.errors),
    ...normalizeErrors(problem?.extensions?.errors),
  ];
  const message = problem?.detail?.trim()
    || details[0]
    || problem?.title?.trim()
    || statusFallbackMessages[status]
    || '请求失败，请稍后重试。';

  return {
    title: status === 400 ? '请求处理失败' : problem?.title || '请求失败',
    message,
    details: details.filter((item) => item !== message),
  };
}
