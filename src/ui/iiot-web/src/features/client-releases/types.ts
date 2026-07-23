import type {
  ClientHostVersionEntryDto,
  ClientPluginVersionEntryDto,
  DeviceClientVersionInventoryDto,
} from './api';

export type ViewMode = 'catalog' | 'inventory' | 'binding';
export type TagTone = 'default' | 'primary' | 'info' | 'success' | 'warning' | 'error';
export type ReleaseKind = 'host' | 'plugin';
export type ReleaseVersionEntry = ClientHostVersionEntryDto | ClientPluginVersionEntryDto;

export interface DeletionRetryOutcome {
  succeeded: boolean;
  auditConfirmed: boolean;
}

// 重试永久删除只有“清理成功且成功审计已确认落库”才算完成；否则保持待恢复。
export function isDeletionRetryComplete(outcome: DeletionRetryOutcome): boolean {
  return outcome.succeeded && outcome.auditConfirmed;
}

export interface ReleaseCatalogRow {
  key: string;
  kind: ReleaseKind;
  kindLabel: string;
  componentName: string;
  componentCode: string;
  componentId: string;
  currentVersion: ReleaseVersionEntry;
  historyVersions: ReleaseVersionEntry[];
}

export interface ReleaseDetail {
  kind: ReleaseKind;
  kindLabel: string;
  componentName: string;
  componentCode: string;
  version: string;
  statusText: string;
  statusTone: TagTone;
  publishedAt: string;
  packageSize: string;
  releaseNotes: string;
}

export function statusTone(status: string): TagTone {
  const normalized = status.toLowerCase();
  if (normalized === 'published' || normalized === 'latest') return 'success';
  if (normalized === 'normal') return 'success';
  if (normalized === 'running') return 'success';
  if (normalized === 'starting') return 'info';
  if (normalized === 'stopped' || normalized === 'runtimeheartbeatstale') return 'warning';
  if (normalized === 'updateavailable') return 'warning';
  if (normalized === 'incompatible' || normalized === 'archived' || normalized === 'deleted' || normalized === 'deletefailed') return 'error';
  if (normalized === 'offline') return 'error';
  if (normalized === 'deprecated') return 'warning';
  if (normalized === 'missingreport' || normalized === 'missingruntimeheartbeat' || normalized === 'norelease') return 'default';
  return 'info';
}

export function statusText(status: string): string {
  return {
    Draft: '草稿',
    Published: '已发布',
    Deprecated: '已弃用',
    Archived: '已归档',
    DeleteRequested: '删除中',
    Deleted: '已删除',
    DeleteFailed: '删除失败',
    Latest: '已最新',
    UpdateAvailable: '可更新',
    Incompatible: '不兼容',
    MissingReport: '未上报',
    MissingRuntimeHeartbeat: '无运行心跳',
    RuntimeHeartbeatStale: '超过24h未运行',
    Starting: '启动中',
    Running: '运行中',
    Stopped: '已停止',
    Unknown: '未知',
    NoRelease: '无发布',
    Offline: '上报超时',
    Normal: '正常',
  }[status] || status;
}

export function formatSize(size: number): string {
  if (!Number.isFinite(size) || size <= 0) return '-';
  if (size >= 1024 * 1024) return `${(size / 1024 / 1024).toFixed(1)} MB`;
  if (size >= 1024) return `${(size / 1024).toFixed(1)} KB`;
  return `${size} B`;
}

export function formatDate(value?: string | null): string {
  if (!value) return '-';
  return new Date(value).toLocaleString();
}

export function formatReleaseNotes(value?: string | null, fallback = '-'): string {
  const text = value?.trim();
  return text && text.length > 0 ? text : fallback;
}

export function formatCurrentVersion(row: DeviceClientVersionInventoryDto): string {
  return row.hostVersion || row.currentVersion || '-';
}

export function pickCurrentVersion<T extends ReleaseVersionEntry>(versions: T[]): T | null {
  return versions.find((version) => version.status.toLowerCase() === 'published')
    ?? versions.find((version) => version.status.toLowerCase() === 'deprecated')
    ?? versions.find((version) => version.status.toLowerCase() === 'draft')
    ?? null;
}
