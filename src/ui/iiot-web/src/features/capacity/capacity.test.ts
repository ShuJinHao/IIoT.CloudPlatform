import { describe, expect, it } from 'vitest';
import { ResultStatus } from '../../core/types/api';
import { Permissions } from '../../types/permissions';
import { resolveCapacityLoadError } from './errors';
import { buildCapacityCsv, capacityExportFileName } from './export';
import { capacityRoutes } from './routes';
import {
  CapacityPayloadError,
  createPlcOptions,
  filterRowsByPlc,
  mapHourlyRows,
  mapMonthRows,
  mapYearRows,
  monthDateRange,
  rateAccent,
  summarizeRows,
  yearDateRange,
} from './types';

const rangeRow = (date: string, total: number, ok: number, plcName: string) => ({
  date,
  totalCount: total,
  okCount: ok,
  ngCount: total - ok,
  dayShiftTotal: total,
  dayShiftOk: ok,
  dayShiftNg: total - ok,
  nightShiftTotal: 0,
  nightShiftOk: 0,
  nightShiftNg: 0,
  plcName,
});

describe('capacity feature', () => {
  it('guards capacity routes with device read permission', () => {
    expect(capacityRoutes.map((route) => route.meta?.requiredPermission)).toEqual([
      Permissions.Device.Read,
      Permissions.Device.Read,
    ]);
  });

  it('keeps rate accent thresholds explicit', () => {
    expect(rateAccent(96)).toBe('success');
    expect(rateAccent(90)).toBe('warn');
    expect(rateAccent(60)).toBe('error');
  });

  it('maps the same time bucket for independent PLC rows with unique identities', () => {
    const rows = mapHourlyRows('2026-07-26', [
      { hour: 8, minute: 30, timeLabel: '08:30', shiftCode: 'D', totalCount: 10, okCount: 9, ngCount: 1, plcName: 'MES-PLC-A' },
      { hour: 8, minute: 30, timeLabel: '08:30', shiftCode: 'D', totalCount: 7, okCount: 7, ngCount: 0, plcName: 'MES-PLC-B' },
    ]);
    expect(rows.map((row) => `${row.bucketKey}-${row.plcKey}-${row.shift}`)).toEqual([
      '2026-07-26-08:30-MES-PLC-A-D',
      '2026-07-26-08:30-MES-PLC-B-D',
    ]);
    expect(summarizeRows(rows)).toMatchObject({ total: 17, ok: 16, ng: 1 });
    expect(summarizeRows(rows).ratePercent).toBeCloseTo(16 / 17 * 100);
  });

  it('preserves real zero rows and separates PLC filtering from API loading', () => {
    const rows = mapMonthRows('2026-07', [
      rangeRow('2026-07-01', 0, 0, 'MES-PLC-A'),
      rangeRow('2026-07-01', 4, 3, 'MES-PLC-B'),
    ]);
    expect(rows).toHaveLength(2);
    expect(filterRowsByPlc(rows, 'MES-PLC-A')).toMatchObject([{ total: 0, ok: 0, ng: 0 }]);
    expect(createPlcOptions(rows)).toEqual([
      { label: 'MES-PLC-A', value: 'MES-PLC-A' },
      { label: 'MES-PLC-B', value: 'MES-PLC-B' },
    ]);
  });

  it('rejects corrupted and out-of-range payloads instead of showing them as empty', () => {
    expect(() => mapMonthRows('2026-07', [
      rangeRow('2026-06-30', 4, 3, 'MES-PLC-A'),
    ])).toThrow(CapacityPayloadError);
    expect(() => mapHourlyRows('2026-07-26', [
      { hour: 8, minute: 30, timeLabel: '08:30', shiftCode: 'D', totalCount: 10, okCount: 9, ngCount: 2, plcName: 'MES-PLC-A' },
    ])).toThrow('无法对账');
  });

  it('builds year rows only from actual months without fabricated zero months', () => {
    const rows = mapYearRows(2026, [
      rangeRow('2026-06-01', 10, 9, 'MES-PLC-A'),
      rangeRow('2026-06-02', 5, 5, 'MES-PLC-A'),
      rangeRow('2026-07-01', 20, 18, 'MES-PLC-B'),
    ]);
    expect(rows).toHaveLength(2);
    expect(rows[0]).toMatchObject({ label: '6 月', plcName: 'MES-PLC-A', total: 15, ok: 14 });
    expect(rows[1]).toMatchObject({ label: '7 月', plcName: 'MES-PLC-B', total: 20, ok: 18 });
  });

  it('derives month and year API ranges', () => {
    expect(monthDateRange('2026-02')).toEqual({
      startDate: '2026-02-01',
      endDate: '2026-02-28',
    });
    expect(yearDateRange(2026)).toEqual({
      startDate: '2026-01-01',
      endDate: '2026-12-31',
    });
  });

  it('exports only the supplied filtered rows with domain-specific headers', () => {
    const row = mapMonthRows('2026-07', [rangeRow('2026-07-01', 4, 3, 'MES,PLC-A')])[0]!;
    const csv = buildCapacityCsv([row]);
    expect(csv.startsWith('\uFEFFPLC 名称,时间范围,班次,完工弹夹数')).toBe(true);
    expect(csv).toContain('"MES,PLC-A",2026-07-01,—,4,3,1,75.00%');
    expect(capacityExportFileName('设备/A', 'month', '2026-07', '全部 PLC'))
      .toBe('设备-A-2026-07-全部-PLC-月完工弹夹明细.csv');
  });

  it('distinguishes payload failures from API failures', async () => {
    await expect(resolveCapacityLoadError(new CapacityPayloadError('缓存格式错误')))
      .resolves.toMatchObject({ kind: 'payload', title: '产能数据解析失败' });
    await expect(resolveCapacityLoadError({
      isSuccess: false,
      status: ResultStatus.Forbidden,
      errors: ['无权查看'],
    })).resolves.toEqual({
      kind: 'api',
      title: '禁止访问',
      message: '无权查看',
    });
  });
});
