export const CAPACITY_PAGE_SIZE = 10;
export const UNASSIGNED_PLC_KEY = '__unassigned_plc__';

export type RateAccent = 'success' | 'warn' | 'error';
export type CapacityQueryMode = 'day' | 'month' | 'year';

export interface CapacityDetailRow {
  bucketKey: string;
  period: string;
  label: string;
  plcKey: string;
  plcName: string;
  shift: string;
  total: number;
  ok: number;
  ng: number;
  rate: number;
}

export interface CapacitySummary {
  total: number;
  ok: number;
  ng: number;
  ratePercent: number;
}

export class CapacityPayloadError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'CapacityPayloadError';
  }
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

function requireArray(value: unknown, context: string): unknown[] {
  if (!Array.isArray(value)) {
    throw new CapacityPayloadError(`${context}不是数组。`);
  }
  return value;
}

function requireRecord(value: unknown, context: string): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new CapacityPayloadError(`${context}不是对象。`);
  }
  return value as Record<string, unknown>;
}

function requireCount(item: Record<string, unknown>, key: string, context: string): number {
  const value = item[key];
  if (!Number.isInteger(value) || (value as number) < 0) {
    throw new CapacityPayloadError(`${context}.${key}必须是非负整数。`);
  }
  return value as number;
}

function requireString(item: Record<string, unknown>, key: string, context: string): string {
  const value = item[key];
  if (typeof value !== 'string' || !value.trim()) {
    throw new CapacityPayloadError(`${context}.${key}不能为空。`);
  }
  return value.trim();
}

function readPlc(item: Record<string, unknown>, context: string) {
  const value = item.plcName;
  if (value !== null && value !== undefined && typeof value !== 'string') {
    throw new CapacityPayloadError(`${context}.plcName格式无效。`);
  }
  const plcName = typeof value === 'string' ? value.trim() : '';
  return {
    plcKey: plcName || UNASSIGNED_PLC_KEY,
    plcName: plcName || '—',
  };
}

function readCounts(item: Record<string, unknown>, context: string) {
  const total = requireCount(item, 'totalCount', context);
  const ok = requireCount(item, 'okCount', context);
  const ng = requireCount(item, 'ngCount', context);
  if (total !== ok + ng) {
    throw new CapacityPayloadError(`${context}的完工、合格和不合格弹夹数无法对账。`);
  }
  return {
    total,
    ok,
    ng,
    rate: total > 0 ? (ok / total) * 100 : 0,
  };
}

interface ParsedHourlyItem {
  hour: number;
  minute: number;
  shiftCode: string;
  timeLabel: string;
  plcKey: string;
  plcName: string;
  total: number;
  ok: number;
  ng: number;
  rate: number;
}

interface ParsedRangeItem {
  date: string;
  plcKey: string;
  plcName: string;
  total: number;
  ok: number;
  ng: number;
  rate: number;
}

function parseHourlyItem(value: unknown, index: number): ParsedHourlyItem {
  const context = `小时产能[${index}]`;
  const item = requireRecord(value, context);
  const hour = requireCount(item, 'hour', context);
  const minute = requireCount(item, 'minute', context);
  if (hour > 23 || minute > 59) {
    throw new CapacityPayloadError(`${context}的时间桶超出有效范围。`);
  }
  const shiftCode = requireString(item, 'shiftCode', context);
  const timeLabel = requireString(item, 'timeLabel', context);
  const counts = readCounts(item, context);
  const plc = readPlc(item, context);
  return {
    hour,
    minute,
    shiftCode,
    timeLabel,
    ...counts,
    ...plc,
  };
}

function parseRangeItem(value: unknown, index: number): ParsedRangeItem {
  const context = `范围产能[${index}]`;
  const item = requireRecord(value, context);
  const date = requireString(item, 'date', context);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) {
    throw new CapacityPayloadError(`${context}.date格式无效。`);
  }
  const counts = readCounts(item, context);
  const plc = readPlc(item, context);
  const dayTotal = requireCount(item, 'dayShiftTotal', context);
  const dayOk = requireCount(item, 'dayShiftOk', context);
  const dayNg = requireCount(item, 'dayShiftNg', context);
  const nightTotal = requireCount(item, 'nightShiftTotal', context);
  const nightOk = requireCount(item, 'nightShiftOk', context);
  const nightNg = requireCount(item, 'nightShiftNg', context);
  if (dayTotal !== dayOk + dayNg || nightTotal !== nightOk + nightNg) {
    throw new CapacityPayloadError(`${context}的班次合格与不合格弹夹数无法对账。`);
  }
  if (counts.total !== dayTotal + nightTotal) {
    throw new CapacityPayloadError(`${context}的班次完工弹夹数与日汇总无法对账。`);
  }
  return {
    date,
    ...counts,
    ...plc,
  };
}

export function mapHourlyRows(date: string, payload: unknown): CapacityDetailRow[] {
  return requireArray(payload, '小时产能响应').map((value, index) => {
    const item = parseHourlyItem(value, index);
    return {
      bucketKey: `${date}-${String(item.hour).padStart(2, '0')}:${String(item.minute).padStart(2, '0')}`,
      period: `${date} ${item.timeLabel}`,
      label: item.timeLabel,
      plcKey: item.plcKey,
      plcName: item.plcName,
      shift: item.shiftCode,
      total: item.total,
      ok: item.ok,
      ng: item.ng,
      rate: item.rate,
    };
  });
}

export function mapMonthRows(month: string, payload: unknown): CapacityDetailRow[] {
  return requireArray(payload, '月产能响应').map((value, index) => {
    const item = parseRangeItem(value, index);
    if (!item.date.startsWith(`${month}-`)) {
      throw new CapacityPayloadError(`月产能响应包含范围外日期：${item.date}。`);
    }
    return {
      bucketKey: item.date,
      period: item.date,
      label: item.date.slice(5),
      plcKey: item.plcKey,
      plcName: item.plcName,
      shift: '',
      total: item.total,
      ok: item.ok,
      ng: item.ng,
      rate: item.rate,
    };
  });
}

export function mapYearRows(year: number, payload: unknown): CapacityDetailRow[] {
  const groups = new Map<string, CapacityDetailRow>();
  requireArray(payload, '年产能响应').forEach((value, index) => {
    const item = parseRangeItem(value, index);
    if (!item.date.startsWith(`${year}-`)) {
      throw new CapacityPayloadError(`年产能响应包含范围外日期：${item.date}。`);
    }
    const month = item.date.slice(0, 7);
    const key = `${month}|${item.plcKey}`;
    const current = groups.get(key) ?? {
      bucketKey: month,
      period: month,
      label: `${Number(month.slice(5))} 月`,
      plcKey: item.plcKey,
      plcName: item.plcName,
      shift: '',
      total: 0,
      ok: 0,
      ng: 0,
      rate: 0,
    };
    current.total += item.total;
    current.ok += item.ok;
    current.ng += item.ng;
    current.rate = current.total > 0 ? (current.ok / current.total) * 100 : 0;
    groups.set(key, current);
  });
  return [...groups.values()].sort((left, right) =>
    left.bucketKey.localeCompare(right.bucketKey)
    || left.plcName.localeCompare(right.plcName, 'zh-CN'));
}

export function filterRowsByPlc(
  rows: CapacityDetailRow[],
  plcKey: string | null,
): CapacityDetailRow[] {
  if (!plcKey) return rows;
  return rows.filter((row) => row.plcKey === plcKey);
}

export function summarizeRows(rows: CapacityDetailRow[]): CapacitySummary {
  const total = rows.reduce((sum, row) => sum + row.total, 0);
  const ok = rows.reduce((sum, row) => sum + row.ok, 0);
  const ng = rows.reduce((sum, row) => sum + row.ng, 0);
  return {
    total,
    ok,
    ng,
    ratePercent: total > 0 ? (ok * 100) / total : 0,
  };
}

export function createPlcOptions(rows: CapacityDetailRow[]) {
  return [...new Map(rows.map((row) => [
    row.plcKey,
    { label: row.plcName, value: row.plcKey },
  ])).values()].sort((left, right) =>
    left.label.localeCompare(right.label, 'zh-CN'));
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
