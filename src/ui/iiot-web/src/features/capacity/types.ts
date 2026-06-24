import type { DailyRangeSummaryDto, DailySummaryItem } from './api';

export const CAPACITY_PAGE_SIZE = 10;

export type RateAccent = 'success' | 'warn' | 'error';
export type CapacityQueryMode = 'day' | 'month' | 'year';

export interface CapacityDetailRow {
  label: string;
  shift: string;
  total: number;
  ok: number;
  ng: number;
  rate: number;
}

export const todayLocal = () => {
  const date = new Date();
  return [
    date.getFullYear(),
    String(date.getMonth() + 1).padStart(2, '0'),
    String(date.getDate()).padStart(2, '0'),
  ].join('-');
};

export const thisMonth = () => {
  const date = new Date();
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
};

export const formatInt = (value: number) => value.toLocaleString('zh-CN');

export const rateAccent = (rate: number): RateAccent => {
  if (rate >= 95) return 'success';
  if (rate >= 85) return 'warn';
  return 'error';
};

const readNumber = (
  source: Record<string, unknown>,
  keys: string[],
  fallback = 0,
) => {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'number' && Number.isFinite(value)) return value;
    if (typeof value === 'string' && value.trim()) {
      const parsed = Number(value);
      if (Number.isFinite(parsed)) return parsed;
    }
  }
  return fallback;
};

const readString = (
  source: Record<string, unknown>,
  keys: string[],
  fallback = '',
) => {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'string' && value.trim()) return value;
  }
  return fallback;
};

export function mapHourlyDetailRow(source: unknown): CapacityDetailRow {
  const item = source && typeof source === 'object'
    ? source as Record<string, unknown>
    : {};
  const hour = readNumber(item, ['hour', 'Hour']);
  const minute = readNumber(item, ['minute', 'Minute']);
  const total = readNumber(item, ['totalCount', 'total_count', 'TotalCount']);
  const ok = readNumber(item, ['okCount', 'ok_count', 'OkCount']);
  const ng = readNumber(item, ['ngCount', 'ng_count', 'NgCount']);

  return {
    label: readString(
      item,
      ['timeLabel', 'time_label', 'TimeLabel'],
      `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`,
    ),
    shift: readString(item, ['shiftCode', 'shift_code', 'ShiftCode']),
    total,
    ok,
    ng,
    rate: total > 0 ? (ok / total) * 100 : 0,
  };
}

export function createDailyFallbackRows(
  summary: DailySummaryItem | null,
  date: string,
): CapacityDetailRow[] {
  if (!summary) return [];

  const rows: CapacityDetailRow[] = [
    {
      label: '白班 08:30-20:30',
      shift: 'D',
      total: summary.dayShiftTotal ?? 0,
      ok: summary.dayShiftOk ?? 0,
      ng: summary.dayShiftNg ?? 0,
      rate: summary.dayShiftTotal > 0
        ? (summary.dayShiftOk / summary.dayShiftTotal) * 100
        : 0,
    },
    {
      label: '夜班 20:30-08:30',
      shift: 'N',
      total: summary.nightShiftTotal ?? 0,
      ok: summary.nightShiftOk ?? 0,
      ng: summary.nightShiftNg ?? 0,
      rate: summary.nightShiftTotal > 0
        ? (summary.nightShiftOk / summary.nightShiftTotal) * 100
        : 0,
    },
  ].filter((row) => row.total > 0);

  if (rows.length > 0 || summary.totalCount <= 0) return rows;

  return [{
    label: date,
    shift: '-',
    total: summary.totalCount,
    ok: summary.okCount,
    ng: summary.ngCount,
    rate: summary.totalCount > 0
      ? (summary.okCount / summary.totalCount) * 100
      : 0,
  }];
}

export function mapMonthRows(
  month: string,
  list: DailyRangeSummaryDto[],
): CapacityDetailRow[] {
  const [, monthText] = month.split('-');
  return list
    .filter((item) => (item.totalCount ?? 0) > 0)
    .map((item) => {
      const total = item.totalCount ?? 0;
      const ok = item.okCount ?? 0;
      const ng = item.ngCount ?? 0;
      const day = item.date?.slice(8, 10) ?? '';
      return {
        label: `${monthText}-${day}`,
        shift: '',
        total,
        ok,
        ng,
        rate: total > 0 ? (ok / total) * 100 : 0,
      };
    });
}

export function mapYearRows(
  year: number,
  list: DailyRangeSummaryDto[],
): CapacityDetailRow[] {
  const byMonth: Record<number, { total: number; ok: number; ng: number }> = {};
  for (let month = 1; month <= 12; month += 1) {
    byMonth[month] = { total: 0, ok: 0, ng: 0 };
  }

  for (const item of list) {
    const month = parseInt((item.date || '').slice(5, 7), 10);
    if (!byMonth[month]) continue;
    byMonth[month].total += item.totalCount ?? 0;
    byMonth[month].ok += item.okCount ?? 0;
    byMonth[month].ng += item.ngCount ?? 0;
  }

  return Object.entries(byMonth).map(([month, value]) => ({
    label: `${month} 月`,
    shift: '',
    total: value.total,
    ok: value.ok,
    ng: value.ng,
    rate: value.total > 0 ? (value.ok / value.total) * 100 : 0,
  }));
}

export function monthDateRange(month: string) {
  const [year, monthNumber] = month.split('-').map(Number) as [number, number];
  const monthText = String(monthNumber).padStart(2, '0');
  const lastDay = new Date(year, monthNumber, 0).getDate();
  return {
    startDate: `${year}-${monthText}-01`,
    endDate: `${year}-${monthText}-${String(lastDay).padStart(2, '0')}`,
  };
}

export function yearDateRange(year: number) {
  return {
    startDate: `${year}-01-01`,
    endDate: `${year}-12-31`,
  };
}
