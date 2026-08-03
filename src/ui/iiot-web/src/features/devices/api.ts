import http from '../../core/http/httpClient';
import type { PagedList, Pagination } from '../../core/types/pagination';

export interface DeviceListItemDto {
  id: string;
  deviceName: string;
  code: string;
  processId: string;
}

export interface DeviceSelectDto {
  id: string;
  deviceName: string;
  code: string;
  processId: string;
}

export interface ScopedDeviceSelectDto extends DeviceSelectDto {
  processCode: string;
  processName: string;
}

export interface EmployeeAccessDeviceCandidateDto {
  id: string;
  deviceName: string;
}

export interface DeviceStatusSummaryDto {
  total: number;
  online: number;
  warning: number;
  error: number;
  offline: number;
  generatedAt: string;
  softwareStatus?: string | null;
  issue?: string | null;
}

export interface RegisterDevicePayload {
  deviceName: string;
  processId: string;
}

export interface CreateDeviceResultDto {
  id: string;
  code: string;
}

export interface UpdateDeviceProfilePayload {
  deviceName: string;
}

export interface DeviceLedgerProcessOptionDto {
  id: string;
  processCode: string;
  processName: string;
}

export interface DeviceProcessMigrationProcessDto {
  id: string;
  processCode: string;
  processName: string;
}

export interface DeviceProcessMigrationRelatedCountsDto {
  recipes: number;
  capacities: number;
  deviceLogs: number;
  passStations: number;
  clientStates: number;
  clientVersionSnapshots: number;
  clientPluginVersions: number;
  runtimeHeartbeats: number;
  uploadReceiveRegistrations: number;
  employeeDeviceAccesses: number;
  refreshTokenSessions: number;
  edgeHostPlcRuntimeStates: number;
  totalAssociatedRows: number;
}

export interface DeviceProcessMigrationBlockerDto {
  code: string;
  message: string;
  count: number;
}

export interface DeviceProcessMigrationImpactDto {
  deviceId: string;
  deviceName: string;
  clientCode: string;
  sourceProcess: DeviceProcessMigrationProcessDto;
  targetProcess: DeviceProcessMigrationProcessDto;
  rowVersion: number;
  relatedCounts: DeviceProcessMigrationRelatedCountsDto;
  blockers: DeviceProcessMigrationBlockerDto[];
  confirmationText: string;
  canMigrate: boolean;
}

export interface MigrateDeviceProcessPayload {
  expectedSourceProcessId: string;
  targetProcessId: string;
  expectedRowVersion: number;
  confirmationText: string;
}

export interface DeviceProcessMigrationResultDto {
  deviceId: string;
  sourceProcessId: string;
  targetProcessId: string;
  rowVersion: number;
}

export interface DeviceDeletionImpactDto {
  deviceId: string;
  deviceName: string;
  clientCode: string;
  processId: string;
  recipes: number;
  capacities: number;
  deviceLogs: number;
  passStations: number;
  clientStates: number;
  clientVersionSnapshots: number;
  clientPluginVersions: number;
  runtimeHeartbeats: number;
  uploadReceiveRegistrations: number;
  employeeDeviceAccesses: number;
  refreshTokenSessions: number;
  edgeHostPlcRuntimeStates: number;
  totalAssociatedRows: number;
}

const basePath = '/human/devices';

export const getDevicePagedListApi = (params: {
  PaginationParams?: Pagination;
  Keyword?: string;
  ProcessId?: string;
}) => {
  return http.get<PagedList<DeviceListItemDto>>(basePath, {
    params: {
      'PaginationParams.PageNumber': params.PaginationParams?.PageNumber ?? 1,
      'PaginationParams.PageSize': params.PaginationParams?.PageSize ?? 10,
      Keyword: params.Keyword || undefined,
      ProcessId: params.ProcessId || undefined,
    },
  });
};

export const getDeviceLedgerProcessOptionsApi = () => {
  return http.get<DeviceLedgerProcessOptionDto[]>(`${basePath}/processes/select`, {
    inlineFeedback: true,
  });
};

export const getAllActiveDevicesApi = () => {
  return http.get<DeviceSelectDto[]>(`${basePath}/all`);
};

export const getScopedDeviceSelectApi = (options?: { inlineFeedback?: boolean }) => {
  return http.get<ScopedDeviceSelectDto[]>(`${basePath}/select`, {
    inlineFeedback: options?.inlineFeedback,
  });
};

export const getEmployeeAccessDeviceCandidatesApi = () => {
  return http.get<EmployeeAccessDeviceCandidateDto[]>(
    `${basePath}/employee-access-candidates`,
  );
};

export const getDeviceStatusSummaryApi = (params?: { deviceId?: string }) => {
  return http.get<DeviceStatusSummaryDto>(`${basePath}/status-summary`, {
    inlineFeedback: true,
    params: {
      deviceId: params?.deviceId || undefined,
    },
  });
};

export const registerDeviceApi = (payload: RegisterDevicePayload) => {
  return http.post<CreateDeviceResultDto>(basePath, payload);
};

export const updateDeviceProfileApi = (id: string, payload: UpdateDeviceProfilePayload) => {
  return http.put<boolean>(`${basePath}/${id}`, payload);
};

export const deleteDeviceApi = (id: string) => {
  return http.delete<boolean>(`${basePath}/${id}`);
};

export const getDeviceDeletionImpactApi = (id: string) => {
  return http.get<DeviceDeletionImpactDto>(`${basePath}/${id}/deletion-impact`);
};

export const getDeviceProcessMigrationImpactApi = (
  id: string,
  targetProcessId: string,
) => {
  return http.get<DeviceProcessMigrationImpactDto>(
    `${basePath}/${id}/process-migration-impact`,
    {
      inlineFeedback: true,
      params: { targetProcessId },
    },
  );
};

export const migrateDeviceProcessApi = (
  id: string,
  payload: MigrateDeviceProcessPayload,
) => {
  return http.post<DeviceProcessMigrationResultDto>(
    `${basePath}/${id}/process-migration`,
    payload,
  );
};
