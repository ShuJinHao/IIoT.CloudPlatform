import { describe, expect, it } from 'vitest';
import { dashboardRoutes } from './routes';
import {
  formatEventTime,
  mapDashboardEvent,
  toEventLabel,
  toEventSeverity,
  todayIsoDate,
} from './types';

describe('dashboard feature', () => {
  it('keeps the dashboard route as the default authenticated child route', () => {
    expect(dashboardRoutes[0]!.path).toBe('');
    expect(dashboardRoutes[0]!.name).toBe('Dashboard');
    expect(dashboardRoutes[0]!.meta?.requiresAuth).toBe(true);
  });

  it('formats current day for capacity aggregate API calls', () => {
    expect(todayIsoDate(new Date('2026-06-24T08:00:00+08:00'))).toBe('2026-06-24');
  });

  it('normalizes alert labels and severities', () => {
    expect(toEventSeverity('ERROR')).toBe('error');
    expect(toEventSeverity('WARNING')).toBe('warn');
    expect(toEventSeverity('INFO')).toBe('info');
    expect(toEventLabel('INFORMATION')).toBe('INFO');
  });

  it('maps recent logs without inventing data', () => {
    const event = mapDashboardEvent({
      id: '1',
      deviceId: 'device-abcdef',
      deviceName: '',
      level: 'WARN',
      message: '温度告警',
      logTime: '2026-06-24T10:10:10Z',
      receivedAt: '2026-06-24T10:10:12Z',
    }, 'zh-CN');
    expect(event.deviceCode).toBe('device-a');
    expect(event.label).toBe('WARN');
    expect(formatEventTime('not-a-date', 'zh-CN')).toBe('--:--:--');
  });
});
