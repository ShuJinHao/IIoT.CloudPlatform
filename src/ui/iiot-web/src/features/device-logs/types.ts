import type { PagedMetaData } from '../../core/types/pagination';

export const DEVICE_LOG_PAGE_SIZE = 20;

export type DeviceLogQueryMode =
  | 'level'
  | 'keyword'
  | 'date'
  | 'time-range'
  | 'date-keyword';

export interface DeviceLogFilters {
  level: string | null;
  keyword: string;
  date: string;
  startTime: string;
  endTime: string;
}

export const queryModes: Array<{ key: DeviceLogQueryMode; label: string }> = [
  { key: 'level', label: '按级别' },
  { key: 'keyword', label: '按关键字' },
  { key: 'date', label: '按日期' },
  { key: 'time-range', label: '按时间范围' },
  { key: 'date-keyword', label: '日期 + 关键字' },
];

export const levelOptions = [
  { label: 'INFO', value: 'INFO' },
  { label: 'WARN', value: 'WARN' },
  { label: 'ERROR', value: 'ERROR' },
];

export const localDate = () => {
  const date = new Date();
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
};

export const defaultStartTime = () => `${localDate()}T00:00`;
export const defaultEndTime = () => `${localDate()}T23:59`;

export const emptyDeviceLogMetaData = (): PagedMetaData => ({
  totalCount: 0,
  pageSize: DEVICE_LOG_PAGE_SIZE,
  currentPage: 1,
  totalPages: 1,
});

export const createDeviceLogFilters = (): DeviceLogFilters => ({
  level: null,
  keyword: '',
  date: localDate(),
  startTime: defaultStartTime(),
  endTime: defaultEndTime(),
});

export const resetDeviceLogDateTime = (filters: DeviceLogFilters) => {
  filters.date = localDate();
  filters.startTime = defaultStartTime();
  filters.endTime = defaultEndTime();
};

export const toUtcIso = (localTime: string) =>
  localTime ? new Date(localTime).toISOString() : '';

export const levelToSeverity = (
  level: string,
): 'info' | 'warn' | 'error' | 'success' => {
  switch (level.toUpperCase()) {
    case 'WARN':
      return 'warn';
    case 'ERROR':
      return 'error';
    default:
      return 'info';
  }
};

export const formatLogTime = (value?: string | null) => {
  if (!value) return '-';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
};

export function validateDeviceLogSearch(
  mode: DeviceLogQueryMode,
  filters: DeviceLogFilters,
) {
  if (mode === 'keyword' && !filters.keyword.trim()) {
    return '请输入关键字。';
  }
  if (mode === 'date' && !filters.date) {
    return '请选择日期。';
  }
  if (mode === 'time-range' && (!filters.startTime || !filters.endTime)) {
    return '请选择完整时间范围。';
  }
  if (mode === 'date-keyword' && (!filters.date || !filters.keyword.trim())) {
    return '请选择日期并输入关键字。';
  }
  return null;
}
