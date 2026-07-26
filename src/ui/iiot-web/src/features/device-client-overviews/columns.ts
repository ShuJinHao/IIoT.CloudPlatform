import { h } from 'vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type {
  DeviceClientOverviewItemDto,
  DeviceClientPluginInventoryDto,
  EdgeHostPlcRuntimeStateDto,
} from './api';
import { formatDateTime } from './types';

interface OverviewColumnOptions {
  onOpenDetail: (row: DeviceClientOverviewItemDto) => void;
  canOpenDetail: boolean;
}

type TagTone = 'default' | 'info' | 'success' | 'warning' | 'error';

export function softwareStatusTone(status?: string | null): TagTone {
  switch ((status ?? '').toLowerCase()) {
    case 'running':
      return 'success';
    case 'starting':
      return 'info';
    case 'stopping':
    case 'stopped':
    case 'runtimeheartbeatstale':
      return 'warning';
    case 'missingruntimeheartbeat':
      return 'default';
    case 'unknown':
      return 'default';
    default:
      return 'info';
  }
}

export function softwareStatusText(status?: string | null): string {
  switch ((status ?? '').toLowerCase()) {
    case 'running':
      return '运行中';
    case 'starting':
      return '启动中';
    case 'stopping':
      return '停止中';
    case 'stopped':
      return '已停止';
    case 'runtimeheartbeatstale':
      return '心跳超时';
    case 'missingruntimeheartbeat':
      return '无运行心跳';
    case 'unknown':
      return '未知';
    default:
      return status || '未知';
  }
}

// 版本/安装/升级状态（installStatus、hostUpdateStatus、插件 updateStatus）沿用发布管理的中文语义。
export function releaseStatusText(status?: string | null): string {
  switch ((status ?? '').toLowerCase()) {
    case 'normal':
      return '正常';
    case 'latest':
      return '已最新';
    case 'updateavailable':
      return '可更新';
    case 'incompatible':
      return '不兼容';
    case 'missingreport':
      return '未上报';
    case 'norelease':
      return '无发布';
    case 'offline':
      return '上报超时';
    default:
      return softwareStatusText(status);
  }
}

export function releaseStatusTone(status?: string | null): TagTone {
  switch ((status ?? '').toLowerCase()) {
    case 'normal':
    case 'latest':
      return 'success';
    case 'updateavailable':
    case 'offline':
      return 'warning';
    case 'incompatible':
      return 'error';
    default:
      return 'default';
  }
}

function runtimeStatusTone(status?: string | null): TagTone {
  switch ((status ?? '').toLowerCase()) {
    case 'connected':
      return 'success';
    case 'disconnected':
      return 'warning';
    case 'faulted':
      return 'error';
    case 'unknown':
      return 'default';
    default:
      return 'info';
  }
}

function runtimeStatusText(status?: string | null): string {
  switch ((status ?? '').toLowerCase()) {
    case 'connected':
      return '已连接';
    case 'disconnected':
      return '未连接';
    case 'faulted':
      return '故障';
    case 'unknown':
      return '未知';
    default:
      return status || '未知';
  }
}

// 主表只渲染冻结契约的窄字段：设备、IP、软件状态、最近可确认宿主版本、异常摘要。
// 「最后运行心跳」是合法 sortBy 但不在窄字段里，只在详情抽屉展示。
export function createOverviewColumns(
  options: OverviewColumnOptions,
): UiDataTableColumn<DeviceClientOverviewItemDto>[] {
  const columns: UiDataTableColumn<DeviceClientOverviewItemDto>[] = [
    {
      title: '设备名称',
      key: 'deviceName',
      minWidth: 200,
      render(row) {
        return h('span', { class: 'cell-name' }, row.deviceName);
      },
    },
    {
      title: 'IP',
      key: 'primaryIpAddress',
      minWidth: 150,
      render(row) {
        return h('span', { class: 'cell-muted' }, row.primaryIpAddress || '-');
      },
    },
    {
      title: '软件状态',
      key: 'softwareStatus',
      width: 130,
      render(row) {
        return h(
          UiTag,
          { size: 'small', bordered: false, type: softwareStatusTone(row.softwareStatus) },
          { default: () => softwareStatusText(row.softwareStatus) },
        );
      },
    },
    {
      title: '最近可确认宿主版本',
      key: 'currentVersion',
      minWidth: 190,
      render(row) {
        return h('span', { class: 'cell-muted' }, row.currentVersion || '-');
      },
    },
    {
      title: '异常摘要',
      key: 'issue',
      minWidth: 220,
      render(row) {
        return h('span', { class: row.issue ? 'cell-error' : 'cell-muted' }, row.issue || '-');
      },
    },
  ];

  if (options.canOpenDetail) {
    columns.push({
      title: '操作',
      key: 'actions',
      width: 110,
      align: 'right',
      render(row) {
        return h(
          UiButton,
          { size: 'tiny', type: 'primary', secondary: true, onClick: () => options.onOpenDetail(row) },
          { default: () => '详情' },
        );
      },
    });
  }

  return columns;
}

export function createPlcRuntimeStateColumns(): UiDataTableColumn<EdgeHostPlcRuntimeStateDto>[] {
  return [
    {
      title: 'PLC',
      key: 'plcCode',
      minWidth: 200,
      render(row) {
        return h('div', { class: 'cell-stack' }, [
          h('code', { class: 'cell-code' }, row.plcCode),
          h('span', { class: 'cell-name' }, row.reportedPlcName || '客户端未上报名称'),
        ]);
      },
    },
    {
      title: '运行状态',
      key: 'runtimeStatus',
      width: 110,
      render(row) {
        return h(
          UiTag,
          { size: 'small', bordered: false, type: runtimeStatusTone(row.runtimeStatus) },
          { default: () => runtimeStatusText(row.runtimeStatus) },
        );
      },
    },
    {
      title: '协议/地址',
      key: 'runtimeAddress',
      minWidth: 200,
      render(row) {
        return h('div', { class: 'cell-stack' }, [
          h('span', { class: 'cell-muted' }, row.runtimeProtocol || '-'),
          h('span', { class: 'cell-mono' }, row.runtimeAddress || '-'),
        ]);
      },
    },
    {
      title: '工位',
      key: 'runtimeStationCode',
      width: 110,
      render(row) {
        return h('span', { class: 'cell-muted' }, row.runtimeStationCode || '-');
      },
    },
    {
      title: '最后错误',
      key: 'lastError',
      minWidth: 200,
      render(row) {
        return h('span', { class: row.lastError ? 'cell-error' : 'cell-muted' }, row.lastError || '-');
      },
    },
    {
      title: '最后上报',
      key: 'lastSeenAtUtc',
      minWidth: 160,
      render(row) {
        return h('span', { class: 'cell-muted' }, formatDateTime(row.lastSeenAtUtc));
      },
    },
  ];
}

export function createPluginInventoryColumns(): UiDataTableColumn<DeviceClientPluginInventoryDto>[] {
  return [
    {
      title: '插件',
      key: 'moduleId',
      minWidth: 160,
      render(row) {
        return h('div', { class: 'cell-stack' }, [
          h('span', { class: 'cell-name' }, row.displayName || row.moduleId),
          h('code', { class: 'cell-mono' }, row.moduleId),
        ]);
      },
    },
    {
      title: '安装事实',
      key: 'version',
      minWidth: 150,
      render(row) {
        return h('div', { class: 'cell-stack' }, [
          h(
            UiTag,
            { size: 'small', bordered: false, type: 'success' },
            { default: () => '已安装' },
          ),
          h('span', { class: 'cell-mono' }, row.version || '版本未上报'),
          h('span', { class: 'cell-muted' }, `Host API ${row.hostApiVersion || '未上报'}`),
        ]);
      },
    },
    {
      title: '配置状态',
      key: 'enabled',
      width: 110,
      render(row) {
        return h(
          UiTag,
          { size: 'small', bordered: false, type: row.enabled ? 'success' : 'default' },
          { default: () => (row.enabled ? '配置启用' : '配置停用') },
        );
      },
    },
    {
      title: '运行状态',
      key: 'runtimeStatus',
      width: 110,
      render() {
        return h(
          UiTag,
          { size: 'small', bordered: false, type: 'default' },
          { default: () => '未上报' },
        );
      },
    },
    {
      title: '最新正式版本',
      key: 'latestPublishedVersion',
      minWidth: 170,
      render(row) {
        return h('div', { class: 'cell-stack' }, [
          h('span', { class: 'cell-mono' }, row.latestPublishedVersion || '无正式发布'),
          h('span', { class: 'cell-muted' }, formatDateTime(row.latestPublishedAtUtc)),
        ]);
      },
    },
    {
      title: '升级与兼容',
      key: 'updateStatus',
      minWidth: 180,
      render(row) {
        return h('div', { class: 'cell-stack' }, [
          h(
            UiTag,
            {
              size: 'small',
              bordered: false,
              type: releaseStatusTone(row.updateStatus),
            },
            { default: () => releaseStatusText(row.updateStatus) },
          ),
          h(
            'span',
            { class: row.compatibilityIssue ? 'cell-error' : 'cell-muted' },
            row.compatibilityIssue || '无兼容性问题',
          ),
        ]);
      },
    },
  ];
}
