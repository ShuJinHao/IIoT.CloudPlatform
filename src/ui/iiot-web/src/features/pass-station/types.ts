import type { PassStationQueryMode } from './api';

export const PAGE_SIZE = 10;

export const queryModeLabels: Record<PassStationQueryMode, string> = {
  'barcode-process': '条码 + 工序',
  'time-process': '时间 + 工序',
  'device-barcode': '设备 + 条码',
  'device-time': '设备 + 时间',
  'device-latest': '设备最近 200 条',
};

export interface PassStationFilters {
  deviceId: string | null;
  barcode: string;
  startTime: string;
  endTime: string;
}

export function localDate() {
  const date = new Date();
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

export function defaultStartTime() {
  return `${localDate()}T00:00`;
}

export function defaultEndTime() {
  return `${localDate()}T23:59`;
}

export function toUtcIso(localTime: string) {
  return localTime ? new Date(localTime).toISOString() : '';
}

export function formatDisplayValue(value: string | null | undefined) {
  if (!value) return '-';
  const date = new Date(value);
  if (!Number.isNaN(date.getTime()) && value.includes('T')) {
    return date.toLocaleString('zh-CN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  }
  return value;
}

export function formatResultText(value: string | null | undefined) {
  const normalized = (value ?? '').trim().toUpperCase();
  if (!normalized) return '-';
  if (normalized === 'OK') return '合格';
  if (normalized === 'NG') return '不合格';
  return value ?? '-';
}
