import type { PagedMetaData } from '../../core/types/pagination';
import type { EdgeHostPlcBindingDto } from './api';

export const EDGE_HOST_PAGE_SIZE = 10;

export type EdgeHostFormMode = 'create' | 'edit';
export type PlcBindingFormMode = 'create' | 'edit';
export type ConfirmTone = 'warning' | 'error';

export interface EdgeHostFormData {
  deviceId: string | null;
  hostName: string;
  remark: string;
}

export interface PlcBindingFormData {
  plcCode: string;
  plcName: string;
  processId: string | null;
  businessDeviceId: string | null;
  stationCode: string;
  protocol: string;
  address: string;
  displayOrder: number | null;
  remark: string;
  enabled: boolean;
}

export interface EdgeHostConfirmDialog {
  show: boolean;
  title: string;
  desc: string;
  confirmText: string;
  tone: ConfirmTone;
  onConfirm: () => Promise<void>;
}

export const emptyEdgeHostMetaData = (): PagedMetaData => ({
  totalCount: 0,
  pageSize: EDGE_HOST_PAGE_SIZE,
  currentPage: 1,
  totalPages: 1,
});

export function normalizeOptionalText(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

export function validateEdgeHostForm(form: EdgeHostFormData, mode: EdgeHostFormMode): string | null {
  if (mode === 'create' && !form.deviceId) return '请选择要绑定的云端设备。';
  if (!form.hostName.trim()) return '上位机名称不能为空。';
  return null;
}

export function validatePlcBindingForm(
  form: PlcBindingFormData,
  mode: PlcBindingFormMode,
): string | null {
  if (mode === 'create' && !form.plcCode.trim()) return 'PLC 编码不能为空。';
  if (!form.plcName.trim()) return 'PLC 名称不能为空。';
  if (form.displayOrder != null && !Number.isInteger(form.displayOrder)) return '排序必须是整数。';
  return null;
}

export function createEmptyPlcBindingForm(): PlcBindingFormData {
  return {
    plcCode: '',
    plcName: '',
    processId: null,
    businessDeviceId: null,
    stationCode: '',
    protocol: '',
    address: '',
    displayOrder: 0,
    remark: '',
    enabled: true,
  };
}

export function copyPlcBindingToForm(binding: EdgeHostPlcBindingDto): PlcBindingFormData {
  return {
    plcCode: binding.plcCode,
    plcName: binding.plcName,
    processId: binding.processId ?? null,
    businessDeviceId: binding.businessDeviceId ?? null,
    stationCode: binding.stationCode ?? '',
    protocol: binding.protocol ?? '',
    address: binding.address ?? '',
    displayOrder: binding.displayOrder,
    remark: binding.remark ?? '',
    enabled: binding.enabled,
  };
}

export function formatDateTime(value?: string | null): string {
  if (!value) return '-';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '-' : date.toLocaleString();
}

export function todayLocal(): string {
  const date = new Date();
  return [
    date.getFullYear(),
    String(date.getMonth() + 1).padStart(2, '0'),
    String(date.getDate()).padStart(2, '0'),
  ].join('-');
}

export function shortId(value?: string | null): string {
  return value ? `${value.slice(0, 8)}...` : '-';
}
