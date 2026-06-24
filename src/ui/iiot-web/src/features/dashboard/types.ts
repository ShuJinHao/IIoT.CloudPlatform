import type { Component } from 'vue';
import type { DeviceLogListItemDto } from '../device-logs/api';

export interface DashboardCard {
  label: string;
  value: string | number;
  helper: string;
  background: string;
  icon: Component;
}

export interface AnalysisLink {
  label: string;
  to: string;
  icon: Component;
}

export interface DashboardEvent {
  time: string;
  message: string;
  deviceCode: string;
  severity: 'info' | 'warn' | 'error';
  label: string;
}

export interface DashboardTrendBar {
  label: string;
  height: string;
  color: string;
}

export interface DashboardStatusRow {
  label: string;
  value: number;
  color: string;
}

export interface DashboardTeamMember {
  name: string;
  role: string;
  initial: string;
  color: string;
}

export function todayIsoDate(date = new Date()): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

export function toEventSeverity(level: string): DashboardEvent['severity'] {
  const normalized = level.trim().toUpperCase();
  if (normalized === 'ERROR' || normalized === 'ERR') return 'error';
  if (normalized === 'WARN' || normalized === 'WARNING') return 'warn';
  return 'info';
}

export function toEventLabel(level: string): string {
  const normalized = level.trim().toUpperCase();
  if (normalized === 'ERROR') return 'ERR';
  if (normalized === 'WARNING') return 'WARN';
  if (normalized === 'INFORMATION') return 'INFO';
  return normalized || 'INFO';
}

export function formatEventTime(value: string, locale: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '--:--:--';
  return date.toLocaleTimeString(locale, {
    hour12: false,
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

export function mapDashboardEvent(
  log: DeviceLogListItemDto,
  locale: string,
): DashboardEvent {
  return {
    time: formatEventTime(log.logTime, locale),
    message: log.message,
    deviceCode: log.deviceName || log.deviceId.slice(0, 8),
    severity: toEventSeverity(log.level),
    label: toEventLabel(log.level),
  };
}
