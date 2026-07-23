import axios from 'axios';
import { normalizeErrors, resolveProblemNotification } from '../../core/http/problemDetails';
import type { DeviceClientOverviewSortBy, DeviceClientOverviewSortDirection } from './api';

export const OVERVIEW_PAGE_SIZE = 10;
export const DEFAULT_SORT_BY: DeviceClientOverviewSortBy = 'deviceName';
export const DEFAULT_SORT_DIRECTION: DeviceClientOverviewSortDirection = 'asc';

// 可排序表头与后端 sortBy 闭集一一对应；后端对未知 sortBy 返回 400，前端只允许这些值。
export const SORTABLE_COLUMNS: ReadonlyArray<{ key: DeviceClientOverviewSortBy; label: string }> = [
  { key: 'deviceName', label: '设备名称' },
  { key: 'softwareStatus', label: '软件状态' },
  { key: 'currentVersion', label: '当前版本' },
  { key: 'lastRuntimeHeartbeatAtUtc', label: '最后运行心跳' },
];

export function formatDateTime(value?: string | null): string {
  if (!value) return '-';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '-' : date.toLocaleString();
}

/** 详情区块内联错误文案：优先后端 ProblemDetails 的 detail/errors/title，不用固定假文案覆盖。 */
export async function resolveInlineErrorMessage(error: unknown, fallback: string): Promise<string> {
  if (axios.isAxiosError(error) && error.response) {
    const problem = error.response.data;
    if (problem && typeof problem === 'object') {
      const notification = resolveProblemNotification(
        error.response.status,
        problem as Parameters<typeof resolveProblemNotification>[1],
      );
      return notification.message;
    }
    const details = normalizeErrors(problem);
    if (details.length > 0) return details[0]!;
  }
  if (error instanceof Error && error.message.trim()) {
    return error.message;
  }
  return fallback;
}
