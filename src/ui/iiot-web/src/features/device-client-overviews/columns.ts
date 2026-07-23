import { h } from 'vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type { DeviceClientOverviewItemDto, EdgeHostPlcRuntimeStateDto } from './api';
import { formatDateTime } from './types';

interface OverviewColumnOptions {
  onOpenDetail: (row: DeviceClientOverviewItemDto) => void;
  canOpenDetail: boolean;
}

type TagTone = 'default' | 'info' | 'success' | 'warning' | 'error';

function softwareStatusTone(status?: string | null): TagTone {
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

// 主表只渲染冻结契约的窄字段：设备、IP、软件状态、当前版本、异常摘要。
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
      title: '当前版本',
      key: 'currentVersion',
      minWidth: 140,
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
