import { describe, expect, it } from 'vitest';
import { Permissions } from '../../types/permissions';
import { capacityRoutes } from './routes';
import {
  createDailyFallbackRows,
  mapHourlyDetailRow,
  mapMonthRows,
  mapYearRows,
  monthDateRange,
  rateAccent,
  yearDateRange,
} from './types';

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

  it('maps hourly payloads across backend naming variants', () => {
    expect(mapHourlyDetailRow({
      TimeLabel: '08:30',
      ShiftCode: 'D',
      TotalCount: 10,
      OkCount: 9,
      NgCount: 1,
    })).toEqual({
      label: '08:30',
      shift: 'D',
      total: 10,
      ok: 9,
      ng: 1,
      rate: 90,
    });
  });

  it('falls back to shift summary without inventing rows', () => {
    expect(createDailyFallbackRows(null, '2026-06-24')).toEqual([]);
    expect(createDailyFallbackRows({
      totalCount: 8,
      okCount: 7,
      ngCount: 1,
      dayShiftTotal: 0,
      dayShiftOk: 0,
      dayShiftNg: 0,
      nightShiftTotal: 0,
      nightShiftOk: 0,
      nightShiftNg: 0,
    }, '2026-06-24')).toHaveLength(1);
  });

  it('derives month and year ranges for API calls', () => {
    expect(monthDateRange('2026-02')).toEqual({
      startDate: '2026-02-01',
      endDate: '2026-02-28',
    });
    expect(yearDateRange(2026)).toEqual({
      startDate: '2026-01-01',
      endDate: '2026-12-31',
    });
  });

  it('maps month and year rows from real summaries', () => {
    const list = [
      { date: '2026-06-01', totalCount: 10, okCount: 9, ngCount: 1, dayShiftTotal: 0, dayShiftOk: 0, dayShiftNg: 0, nightShiftTotal: 0, nightShiftOk: 0, nightShiftNg: 0 },
      { date: '2026-07-01', totalCount: 20, okCount: 18, ngCount: 2, dayShiftTotal: 0, dayShiftOk: 0, dayShiftNg: 0, nightShiftTotal: 0, nightShiftOk: 0, nightShiftNg: 0 },
    ];
    expect(mapMonthRows('2026-06', list).map((row) => row.label)).toEqual(['06-01', '06-01']);
    expect(mapYearRows(2026, list).find((row) => row.label === '6 月')?.total).toBe(10);
    expect(mapYearRows(2026, list).find((row) => row.label === '7 月')?.ok).toBe(18);
  });
});
