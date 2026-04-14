import http from '../../utils/http';
import type { Pagination, PagedMetaData } from '../employee';

export type { Pagination, PagedMetaData };

export interface ProcessListItemDto {
  id: string;
  processCode: string;
  processName: string;
}

export interface ProcessSelectDto {
  id: string;
  processCode: string;
  processName: string;
}

export interface CreateProcessPayload {
  processCode: string;
  processName: string;
}

export interface UpdateProcessPayload {
  processId: string;
  processCode: string;
  processName: string;
}

export interface PagedList<T> {
  items: T[];
  metaData: PagedMetaData;
}

const basePath = '/human/master-data/processes';

export const getProcessPagedListApi = (params: {
  pagination?: Pagination;
  keyword?: string;
}) =>
  http.get<PagedList<ProcessListItemDto>>(basePath, {
    params: {
      PageNumber: params.pagination?.PageNumber ?? 1,
      PageSize: params.pagination?.PageSize ?? 10,
      keyword: params.keyword || undefined,
    },
  });

export const getAllProcessesApi = () =>
  http.get<ProcessSelectDto[]>(`${basePath}/all`);

export const createProcessApi = (payload: CreateProcessPayload) =>
  http.post<string>(basePath, payload);

export const updateProcessApi = (id: string, payload: UpdateProcessPayload) =>
  http.put<boolean>(`${basePath}/${id}`, payload);

export const deleteProcessApi = (id: string) =>
  http.delete<boolean>(`${basePath}/${id}`);
