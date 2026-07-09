import { h } from 'vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type { EdgeHostListItemDto, EdgeHostPlcRuntimeStateDto } from './api';
import { formatDateTime, formatIpAddresses } from './types';

interface EdgeHostColumnOptions {
  onOpenPlcState: (host: EdgeHostListItemDto) => void;
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
    default:
      return 'info';
  }
}

function softwareStatusText(status?: string | null): string {
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
    default:
      return status || '未知';
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

export function createEdgeHostColumns(
  options: EdgeHostColumnOptions,
): UiDataTableColumn<EdgeHostListItemDto>[] {
  return [
    {
      title: '上位机',
      key: 'hostName',
      minWidth: 190,
      render(row) {
        return h('div', { class: 'cell-stack' }, [
          h('span', { class: 'cell-name' }, row.hostName),
          h('code', { class: 'cell-code' }, row.clientCode),
        ]);
      },
    },
    {
      title: 'IP',
      key: 'primaryIpAddress',
      minWidth: 150,
      render(row) {
        return h('span', { class: 'cell-muted' }, formatIpAddresses(row.primaryIpAddress, row.localIpAddresses));
      },
    },
    {
      title: '客户端状态',
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
      minWidth: 150,
      render(row) {
        return h('span', { class: 'cell-muted' }, row.currentVersion || '-');
      },
    },
    {
      title: '最后运行心跳',
      key: 'lastRuntimeHeartbeatAtUtc',
      minWidth: 170,
      render(row) {
        return h('span', { class: 'cell-muted' }, formatDateTime(row.lastRuntimeHeartbeatAtUtc));
      },
    },
    {
      title: 'PLC 状态',
      key: 'plcCount',
      width: 150,
      render(row) {
        const text = row.plcCount > 0
          ? `${row.connectedPlcCount}/${row.plcCount} 已连接`
          : '未上报';
        return h('span', { class: row.faultedPlcCount > 0 ? 'cell-error' : 'cell-count' }, text);
      },
    },
    {
      title: 'PLC 上报时间',
      key: 'lastPlcSeenAtUtc',
      minWidth: 170,
      render(row) {
        return h('span', { class: 'cell-muted' }, formatDateTime(row.lastPlcSeenAtUtc));
      },
    },
    {
      title: '问题',
      key: 'issue',
      minWidth: 210,
      render(row) {
        return h('span', { class: row.issue ? 'cell-error' : 'cell-muted' }, row.issue || '-');
      },
    },
    {
      title: '操作',
      key: 'actions',
      width: 120,
      align: 'right',
      render(row) {
        return h(
          UiButton,
          { size: 'tiny', type: 'primary', secondary: true, onClick: () => options.onOpenPlcState(row) },
          { default: () => 'PLC 状态' },
        );
      },
    },
  ];
}

export function createPlcRuntimeStateColumns(): UiDataTableColumn<EdgeHostPlcRuntimeStateDto>[] {
  return [
    {
      title: 'PLC',
      key: 'plcCode',
      minWidth: 220,
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
      width: 120,
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
      minWidth: 220,
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
      width: 120,
      render(row) {
        return h('span', { class: 'cell-muted' }, row.runtimeStationCode || '-');
      },
    },
    {
      title: '最后错误',
      key: 'lastError',
      minWidth: 220,
      render(row) {
        return h('span', { class: row.lastError ? 'cell-error' : 'cell-muted' }, row.lastError || '-');
      },
    },
    {
      title: '最后上报',
      key: 'lastSeenAtUtc',
      minWidth: 170,
      render(row) {
        return h('span', { class: 'cell-muted' }, formatDateTime(row.lastSeenAtUtc));
      },
    },
  ];
}
