import { h } from 'vue';
import { Archive, ExternalLink, History, Info, ShieldX, Trash2 } from 'lucide-vue-next';
import UiButton from '../../components/ui/UiButton.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import {
  formatDate,
  formatReleaseNotes,
  formatSize,
  statusText,
  statusTone,
  type ReleaseCatalogRow,
  type ReleaseVersionEntry,
} from './types';

interface ReleaseCatalogColumnOptions {
  isPublishRoute: () => boolean;
  canHardDelete: () => boolean;
  onHistory: (row: ReleaseCatalogRow) => void;
  onDetail: (version: ReleaseVersionEntry, row: ReleaseCatalogRow | null) => void;
  onOpenUrl: (url: string) => void;
  onArchive: (version: ReleaseVersionEntry, row: ReleaseCatalogRow | null) => void;
  onDeleteFiles: (version: ReleaseVersionEntry, row: ReleaseCatalogRow | null) => void;
  onHardDelete: (row: ReleaseCatalogRow) => void;
}

export function createReleaseCatalogColumns(
  options: ReleaseCatalogColumnOptions,
): UiDataTableColumn<ReleaseCatalogRow>[] {
  const columns: UiDataTableColumn<ReleaseCatalogRow>[] = [
    {
      title: '组件 / 工序',
      key: 'componentName',
      minWidth: 220,
      render: (row) => h('div', { class: 'release-cell' }, [
        h('strong', row.componentName),
        h('code', row.componentCode),
      ]),
    },
    {
      title: '类型',
      key: 'kind',
      width: 110,
      render: (row) => h(UiTag, {
        type: row.kind === 'host' ? 'default' : 'info',
        size: 'small',
        bordered: false,
      }, () => row.kindLabel),
    },
    { title: '当前版本', key: 'version', width: 120, render: (row) => h('strong', row.currentVersion.version) },
    {
      title: '状态',
      key: 'status',
      width: 100,
      render: (row) => h(UiTag, {
        type: statusTone(row.currentVersion.status),
        size: 'small',
        bordered: false,
      }, () => statusText(row.currentVersion.status)),
    },
    { title: '发布时间', key: 'publishedAtUtc', minWidth: 170, render: (row) => h('span', formatDate(row.currentVersion.publishedAtUtc)) },
    { title: '大小', key: 'packageSize', width: 110, render: (row) => h('span', formatSize(row.currentVersion.packageSize)) },
    {
      title: '更新内容',
      key: 'releaseNotes',
      minWidth: 280,
      render: (row) => h('div', { class: 'release-note-cell' }, [
        h('span', { class: 'release-note' }, formatReleaseNotes(row.currentVersion.releaseNotes)),
        h(UiButton, {
          size: 'tiny',
          secondary: true,
          type: 'info',
          onClick: () => options.onDetail(row.currentVersion, row),
        }, () => [h(Info, { size: 13 }), '详情']),
      ]),
    },
    {
      title: '其他活动版本',
      key: 'otherVersions',
      width: 150,
      render: (row) => row.otherVersions.length > 0
        ? h(UiButton, {
            size: 'tiny',
            secondary: true,
            type: 'info',
            onClick: () => options.onHistory(row),
          }, () => [h(History, { size: 13 }), `查看 ${row.otherVersions.length}`])
        : h('span', { class: 'history-empty' }, '无其他活动版本'),
    },
  ];

  if (options.isPublishRoute()) {
    columns.push({
      title: '操作',
      key: 'actions',
      width: options.canHardDelete() ? 320 : 260,
      align: 'right',
      render: (row) => {
        const buttons = [
          h(UiButton, {
            size: 'tiny',
            secondary: true,
            type: 'primary',
            onClick: () => options.onOpenUrl(row.currentVersion.downloadUrl),
          }, () => [h(ExternalLink, { size: 13 }), '打开']),
          h(UiButton, {
            size: 'tiny',
            secondary: true,
            type: 'warning',
            onClick: () => options.onArchive(row.currentVersion, row),
          }, () => [h(Archive, { size: 13 }), '归档']),
          h(UiButton, {
            size: 'tiny',
            secondary: true,
            type: 'error',
            onClick: () => options.onDeleteFiles(row.currentVersion, row),
          }, () => [h(Trash2, { size: 13 }), '删文件']),
        ];
        if (options.canHardDelete()) {
          buttons.push(h(UiButton, {
            size: 'tiny',
            secondary: true,
            type: 'error',
            onClick: () => options.onHardDelete(row),
          }, () => [h(ShieldX, { size: 13 }), '永久删除']));
        }
        return h('div', { class: 'row-actions' }, buttons);
      },
    });
  }

  return columns;
}

interface HistoryColumnOptions {
  isPublishRoute: () => boolean;
  selectedRow: () => ReleaseCatalogRow | null;
  onDetail: (version: ReleaseVersionEntry, row: ReleaseCatalogRow | null) => void;
  onOpenUrl: (url: string) => void;
  onArchive: (version: ReleaseVersionEntry, row: ReleaseCatalogRow | null) => void;
  onDeleteFiles: (version: ReleaseVersionEntry, row: ReleaseCatalogRow | null) => void;
}

export function createHistoryColumns(
  options: HistoryColumnOptions,
): UiDataTableColumn<ReleaseVersionEntry>[] {
  const columns: UiDataTableColumn<ReleaseVersionEntry>[] = [
    { title: '版本', key: 'version', width: 120, render: (row) => h('strong', row.version) },
    {
      title: '状态',
      key: 'status',
      width: 110,
      render: (row) => h(UiTag, { type: statusTone(row.status), size: 'small', bordered: false }, () => statusText(row.status)),
    },
    { title: '发布时间', key: 'publishedAtUtc', minWidth: 170, render: (row) => h('span', formatDate(row.publishedAtUtc)) },
    {
      title: '更新内容',
      key: 'releaseNotes',
      minWidth: 280,
      render: (row) => h('div', { class: 'release-note-cell' }, [
        h('span', { class: 'release-note' }, formatReleaseNotes(row.releaseNotes)),
        h(UiButton, {
          size: 'tiny',
          secondary: true,
          type: 'info',
          onClick: () => options.onDetail(row, options.selectedRow()),
        }, () => [h(Info, { size: 13 }), '详情']),
      ]),
    },
  ];

  if (options.isPublishRoute()) {
    columns.push({
      title: '操作',
      key: 'actions',
      width: 260,
      align: 'right',
      render: (row) => h('div', { class: 'row-actions' }, [
        h(UiButton, {
          size: 'tiny',
          secondary: true,
          type: 'primary',
          onClick: () => options.onOpenUrl(row.downloadUrl),
        }, () => [h(ExternalLink, { size: 13 }), '打开']),
        h(UiButton, {
          size: 'tiny',
          secondary: true,
          type: 'warning',
          onClick: () => options.onArchive(row, options.selectedRow()),
        }, () => [h(Archive, { size: 13 }), '归档']),
        h(UiButton, {
          size: 'tiny',
          secondary: true,
          type: 'error',
          onClick: () => options.onDeleteFiles(row, options.selectedRow()),
        }, () => [h(Trash2, { size: 13 }), '删文件']),
      ]),
    });
  }

  return columns;
}
