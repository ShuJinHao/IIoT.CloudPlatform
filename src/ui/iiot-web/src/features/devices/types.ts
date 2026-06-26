import type { DeviceDeletionImpactDto } from './api';

export interface DeviceRegisterForm {
  deviceName: string;
  processId: string | null;
}

export interface DeviceEditForm {
  deviceName: string;
}

export interface DeviceConfirmDialogState {
  show: boolean;
  title: string;
  desc: string;
  confirmText: string;
  danger: boolean;
  impact: DeviceDeletionImpactDto | null;
  requiredText: string;
  confirmInput: string;
  onConfirm: () => Promise<void>;
}

export interface DeviceDeletionImpactRow {
  label: string;
  value: number;
}

export function isDeviceDeleteConfirmDisabled(
  requiredText: string,
  confirmInput: string,
): boolean {
  return !!requiredText && confirmInput !== requiredText;
}
