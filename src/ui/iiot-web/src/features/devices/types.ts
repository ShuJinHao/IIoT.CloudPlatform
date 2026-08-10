import type {
  DeviceListItemDto,
  DeviceProcessMigrationImpactDto,
} from './api';

export interface DeviceRegisterForm {
  deviceName: string;
  processId: string | null;
  pluginComponentId: string | null;
}

export interface DeviceEditForm {
  deviceName: string;
}

export interface DeviceProcessMigrationDialogState {
  show: boolean;
  device: DeviceListItemDto | null;
  targetProcessId: string | null;
  impact: DeviceProcessMigrationImpactDto | null;
  loading: boolean;
  error: string;
  confirmInput: string;
}
