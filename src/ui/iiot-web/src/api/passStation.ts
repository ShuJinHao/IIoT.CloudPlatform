import http from '../utils/http';
import type { Pagination, PagedMetaData } from './employee';

export type { Pagination, PagedMetaData };

export type PassStationFieldValue = string | number | boolean | null;

export type PassStationQueryMode =
  | 'barcode-process'
  | 'time-process'
  | 'device-barcode'
  | 'device-time'
  | 'device-latest';

export interface PagedList<T> {
  items: T[];
  metaData: PagedMetaData;
}

export interface PassStationListItemDto {
  id: string;
  deviceId: string;
  barcode: string | null;
  cellResult: string | null;
  completedTime: string | null;
  receivedAt: string | null;
  fields: Record<string, PassStationFieldValue>;
}

export interface PassStationDetailDto {
  id: string;
  deviceId: string;
  barcode: string | null;
  cellResult: string | null;
  completedTime: string | null;
  receivedAt: string | null;
  fields: Record<string, PassStationFieldValue>;
}

export interface GetPassStationListParams {
  typeKey: string;
  mode: PassStationQueryMode;
  pagination?: Pagination;
  processId?: string;
  deviceId?: string;
  barcode?: string;
  startTime?: string;
  endTime?: string;
}

const basePath = '/human/pass-stations';

export const getPassStationListApi = (params: GetPassStationListParams) => {
  return http.get<PagedList<PassStationListItemDto>>(
    `${basePath}/${encodeURIComponent(params.typeKey)}`,
    {
      params: {
        PageNumber: params.pagination?.PageNumber ?? 1,
        PageSize: params.pagination?.PageSize ?? 10,
        mode: params.mode,
        processId: params.processId || undefined,
        deviceId: params.deviceId || undefined,
        barcode: params.barcode || undefined,
        startTime: params.startTime || undefined,
        endTime: params.endTime || undefined,
      },
    },
  );
};

export const getPassStationDetailApi = (typeKey: string, id: string) => {
  return http.get<PassStationDetailDto>(`${basePath}/${encodeURIComponent(typeKey)}/${id}`);
};
