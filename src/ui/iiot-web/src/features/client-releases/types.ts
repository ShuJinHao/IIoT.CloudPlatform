import type {
  ClientHostVersionEntryDto,
  ClientPluginVersionEntryDto,
  DeviceClientVersionInventoryDto,
  UpsertClientHostReleasePayload,
  UpsertClientPluginReleasePayload,
} from './api';

export type ViewMode = 'catalog' | 'inventory' | 'binding';
export type TagTone = 'default' | 'primary' | 'info' | 'success' | 'warning' | 'error';
export type ReleaseKind = 'host' | 'plugin';
export type ReleaseVersionEntry = ClientHostVersionEntryDto | ClientPluginVersionEntryDto;
export type HostReleaseForm = Omit<UpsertClientHostReleasePayload, 'packageSize'> & { packageSize: string };
export type PluginReleaseForm = Omit<UpsertClientPluginReleasePayload, 'packageSize'> & { packageSize: string };

export interface ReleaseCatalogRow {
  key: string;
  kind: ReleaseKind;
  kindLabel: string;
  componentName: string;
  componentCode: string;
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

export interface ReleaseMetadataInput {
  downloadUrl: string;
  sha256: string;
  packageSize: string;
  status: string;
  releaseNotes?: string | null;
}

export const statusOptions = [
  { label: 'Draft', value: 'Draft' },
  { label: 'Published', value: 'Published' },
  { label: 'Deprecated', value: 'Deprecated' },
  { label: 'Archived', value: 'Archived' },
];

const realSha256Pattern = /^[0-9a-f]{64}$/i;
const placeholderSha256Pattern = /^0{64}$/;

export function normalizeOptional(value?: string | null): string | null {
  const trimmed = value?.trim();
  return trimmed && trimmed.length > 0 ? trimmed : null;
}

export function validateReleaseMetadata(value: ReleaseMetadataInput): number | null {
  if (getReleaseMetadataValidationMessage(value)) return null;

  return Number(value.packageSize);
}

export function getReleaseMetadataValidationMessage(value: ReleaseMetadataInput): string | null {
  if (!isValidDownloadUrl(value.downloadUrl)) {
    return '下载地址必须是 http/https 地址，或 /edge-updates/ 下的真实发布路径。';
  }

  const sha256 = value.sha256.trim();
  if (!realSha256Pattern.test(sha256) || placeholderSha256Pattern.test(sha256)) {
    return 'SHA256 必须是 64 位十六进制真实 hash，不能使用全 0 占位值。';
  }

  const packageSize = Number(value.packageSize);
  if (!Number.isInteger(packageSize) || packageSize <= 0) {
    return '包大小必须填写真实正整数，不能为 0。';
  }

  if (value.status === 'Published' && !value.releaseNotes?.trim()) {
    return '发布状态为 Published 时必须填写更新内容。';
  }

  return null;
}

export function isValidDownloadUrl(value: string): boolean {
  const trimmed = value.trim();
  if (trimmed.startsWith('/edge-updates/')) return true;
  try {
    const url = new URL(trimmed);
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
}

export function statusTone(status: string): TagTone {
  const normalized = status.toLowerCase();
  if (normalized === 'published' || normalized === 'latest') return 'success';
  if (normalized === 'normal') return 'success';
  if (normalized === 'updateavailable') return 'warning';
  if (normalized === 'incompatible' || normalized === 'archived') return 'error';
  if (normalized === 'offline') return 'error';
  if (normalized === 'deprecated') return 'warning';
  if (normalized === 'missingreport' || normalized === 'norelease') return 'default';
  return 'info';
}

export function statusText(status: string): string {
  return {
    Draft: '草稿',
    Published: '已发布',
    Deprecated: '已弃用',
    Archived: '已归档',
    Latest: '已最新',
    UpdateAvailable: '可更新',
    Incompatible: '不兼容',
    MissingReport: '未上报',
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
    ?? versions[0]
    ?? null;
}
