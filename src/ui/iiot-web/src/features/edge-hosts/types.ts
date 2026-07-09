import type { PagedMetaData } from '../../core/types/pagination';

export const EDGE_HOST_PAGE_SIZE = 10;

export const emptyEdgeHostMetaData = (): PagedMetaData => ({
  totalCount: 0,
  pageSize: EDGE_HOST_PAGE_SIZE,
  currentPage: 1,
  totalPages: 1,
});

export function formatDateTime(value?: string | null): string {
  if (!value) return '-';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '-' : date.toLocaleString();
}

export function formatIpAddresses(primary?: string | null, localIps: string[] = []): string {
  if (primary) return primary;
  return localIps[0] ?? '-';
}
