import { h } from 'vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type {
  EdgeHostListItemDto,
  EdgeHostPlcBindingDto,
  EdgeHostPlcCapacitySummaryDto,
  EdgeHostPlcRuntimeStateDto,
} from './api';
import { formatDateTime, shortId } from './types';

interface EdgeHostColumnOptions {
  canManage: () => boolean;
  deviceLabel: (deviceId: string) => string;
  onConfigurePlc: (host: EdgeHostListItemDto) => void;
  onEdit: (host: EdgeHostListItemDto) => void;
  onToggle: (host: EdgeHostListItemDto) => void;
  onDelete: (host: EdgeHostListItemDto) => void;
}

interface PlcBindingColumnOptions {
  canManage: () => boolean;
  processLabel: (processId?: string | null) => string;
  deviceLabel: (deviceId?: string | null) => string;
  onEdit: (binding: EdgeHostPlcBindingDto) => void;
  onToggle: (binding: EdgeHostPlcBindingDto) => void;
  onRemove: (binding: EdgeHostPlcBindingDto) => void;
}

interface PlcRuntimeStateColumnOptions {
  processLabel: (processId?: string | null) => string;
  deviceLabel: (deviceId?: string | null) => string;
}

interface PlcCapacitySummaryColumnOptions {
  deviceLabel: (deviceId?: string | null) => string;
}

type TagTone = 'default' | 'info' | 'success' | 'warning' | 'error';

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

function capacityStatusTone(status?: string | null): TagTone {
  switch ((status ?? '').toLowerCase()) {
    case 'ready':
      return 'success';
    case 'nocapacitydata':
      return 'info';
    case 'nobusinessdevice':
    case 'bindingdisabled':
    case 'nodeviceaccess':
      return 'warning';
    default:
      return 'default';
  }
}

function capacityStatusText(status?: string | null): string {
  switch ((status ?? '').toLowerCase()) {
    case 'ready':
      return '有产能';
    case 'nocapacitydata':
      return '无产能';
    case 'nobusinessdevice':
      return '未关联设备';
    case 'bindingdisabled':
      return '绑定禁用';
    case 'nodeviceaccess':
      return '无设备权限';
    default:
      return status || '未知';
  }
}

function formatCount(value?: number | null): string {
  return typeof value === 'number' ? value.toLocaleString('zh-CN') : '-';
}

function formatRate(row: EdgeHostPlcCapacitySummaryDto): string {
  const total = row.summary?.totalCount ?? 0;
  if (!row.summary || total <= 0) return '-';
  return `${((row.summary.okCount / total) * 100).toFixed(1)}%`;
}

export function createEdgeHostColumns(
  options: EdgeHostColumnOptions,
): UiDataTableColumn<EdgeHostListItemDto>[] {
  return [
    {
      title: '上位机',
      key: 'hostName',
      minWidth: 180,
      render(row) {
        return h('div', { class: 'cell-stack' }, [
          h('span', { class: 'cell-name' }, row.hostName),
          h('span', { class: 'cell-muted' }, row.remark || '未填写备注'),
        ]);
      },
    },
    {
      title: 'ClientCode',
      key: 'clientCode',
      minWidth: 190,
      render(row) {
        return h('code', { class: 'cell-code' }, row.clientCode);
      },
    },
    {
      title: '绑定设备',
      key: 'deviceId',
      minWidth: 200,
      render(row) {
        return h('span', { class: 'cell-chip' }, options.deviceLabel(row.deviceId));
      },
    },
    {
      title: '配置状态',
      key: 'enabled',
      width: 110,
      render(row) {
        return h(
          UiTag,
          { size: 'small', bordered: false, type: row.enabled ? 'success' : 'warning' },
          { default: () => (row.enabled ? '已启用' : '已禁用') },
        );
      },
    },
    {
      title: 'PLC 绑定',
      key: 'plcBindingCount',
      width: 130,
      render(row) {
        return h('span', { class: 'cell-count' }, `${row.enabledPlcBindingCount}/${row.plcBindingCount}`);
      },
    },
    {
      title: '更新时间',
      key: 'updatedAtUtc',
      minWidth: 170,
      render(row) {
        return h('span', { class: 'cell-muted' }, formatDateTime(row.updatedAtUtc));
      },
    },
    {
      title: '操作',
      key: 'actions',
      width: 280,
      align: 'right',
      render(row) {
        const actions = [
          h(UiButton, { size: 'tiny', type: 'primary', secondary: true, onClick: () => options.onConfigurePlc(row) }, { default: () => 'PLC 绑定' }),
        ];

        if (options.canManage()) {
          actions.push(
            h(UiButton, { size: 'tiny', type: 'info', secondary: true, onClick: () => options.onEdit(row) }, { default: () => '编辑' }),
            h(UiButton, { size: 'tiny', type: row.enabled ? 'warning' : 'success', secondary: true, onClick: () => options.onToggle(row) }, { default: () => (row.enabled ? '禁用' : '启用') }),
            h(UiButton, { size: 'tiny', type: 'error', secondary: true, onClick: () => options.onDelete(row) }, { default: () => '删除' }),
          );
        }

        return h('div', { class: 'row-actions' }, actions);
      },
    },
  ];
}

export function createPlcCapacitySummaryColumns(
  options: PlcCapacitySummaryColumnOptions,
): UiDataTableColumn<EdgeHostPlcCapacitySummaryDto>[] {
  return [
    {
      title: 'PLC',
      key: 'plcCode',
      minWidth: 220,
      render(row) {
        return h('div', { class: 'cell-stack' }, [
          h('code', { class: 'cell-code' }, row.plcCode),
          h('span', { class: 'cell-name' }, row.plcName),
        ]);
      },
    },
    {
      title: '产能状态',
      key: 'capacityStatus',
      width: 130,
      render(row) {
        return h(
          UiTag,
          { size: 'small', bordered: false, type: capacityStatusTone(row.capacityStatus) },
          { default: () => capacityStatusText(row.capacityStatus) },
        );
      },
    },
    {
      title: '业务设备',
      key: 'businessDeviceId',
      minWidth: 210,
      render(row) {
        return h('span', { class: 'cell-chip' }, options.deviceLabel(row.businessDeviceId));
      },
    },
    {
      title: '总数',
      key: 'totalCount',
      width: 110,
      render(row) {
        return h('span', { class: 'cell-count' }, formatCount(row.summary?.totalCount));
      },
    },
    {
      title: 'OK / NG',
      key: 'okNg',
      width: 130,
      render(row) {
        return h('span', { class: 'cell-muted' }, `${formatCount(row.summary?.okCount)} / ${formatCount(row.summary?.ngCount)}`);
      },
    },
    {
      title: '良率',
      key: 'okRate',
      width: 90,
      render(row) {
        return h('span', { class: 'cell-count' }, formatRate(row));
      },
    },
  ];
}

export function createPlcRuntimeStateColumns(
  options: PlcRuntimeStateColumnOptions,
): UiDataTableColumn<EdgeHostPlcRuntimeStateDto>[] {
  return [
    {
      title: 'PLC',
      key: 'plcCode',
      minWidth: 220,
      render(row) {
        return h('div', { class: 'cell-stack' }, [
          h('code', { class: 'cell-code' }, row.plcCode),
          h('span', { class: 'cell-name' }, row.reportedPlcName || '未上报名称'),
        ]);
      },
    },
    {
      title: '绑定状态',
      key: 'isConfigured',
      width: 120,
      render(row) {
        return h(
          UiTag,
          { size: 'small', bordered: false, type: row.isConfigured ? 'success' : 'warning' },
          { default: () => (row.isConfigured ? '已配置' : '未配置') },
        );
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
      title: '工序 / 业务设备',
      key: 'processId',
      minWidth: 220,
      render(row) {
        return h('div', { class: 'cell-stack' }, [
          h('span', { class: 'cell-chip' }, options.processLabel(row.processId)),
          h('span', { class: 'cell-muted' }, options.deviceLabel(row.businessDeviceId)),
        ]);
      },
    },
    {
      title: '配置地址',
      key: 'configuredAddress',
      minWidth: 200,
      render(row) {
        return h('div', { class: 'cell-stack' }, [
          h('span', { class: 'cell-muted' }, row.configuredProtocol || '-'),
          h('span', { class: 'cell-mono' }, row.configuredAddress || '-'),
        ]);
      },
    },
    {
      title: '上报地址',
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
      title: '最后错误',
      key: 'lastError',
      minWidth: 180,
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

export function createPlcBindingColumns(
  options: PlcBindingColumnOptions,
): UiDataTableColumn<EdgeHostPlcBindingDto>[] {
  return [
    {
      title: 'PLC',
      key: 'plcCode',
      minWidth: 210,
      render(row) {
        return h('div', { class: 'cell-stack' }, [
          h('code', { class: 'cell-code' }, row.plcCode),
          h('span', { class: 'cell-name' }, row.plcName),
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
          { size: 'small', bordered: false, type: row.enabled ? 'success' : 'warning' },
          { default: () => (row.enabled ? '已启用' : '已禁用') },
        );
      },
    },
    {
      title: '工序',
      key: 'processId',
      minWidth: 180,
      render(row) {
        return h('span', { class: 'cell-chip' }, options.processLabel(row.processId));
      },
    },
    {
      title: '业务设备',
      key: 'businessDeviceId',
      minWidth: 190,
      render(row) {
        return h('span', { class: 'cell-chip' }, options.deviceLabel(row.businessDeviceId));
      },
    },
    {
      title: '协议/地址',
      key: 'address',
      minWidth: 180,
      render(row) {
        return h('div', { class: 'cell-stack' }, [
          h('span', { class: 'cell-muted' }, row.protocol || '-'),
          h('span', { class: 'cell-mono' }, row.address || '-'),
        ]);
      },
    },
    {
      title: '工位',
      key: 'stationCode',
      width: 110,
      render(row) {
        return h('span', { class: 'cell-muted' }, row.stationCode || '-');
      },
    },
    {
      title: '排序',
      key: 'displayOrder',
      width: 80,
      render(row) {
        return h('span', { class: 'cell-count' }, String(row.displayOrder));
      },
    },
    {
      title: '更新时间',
      key: 'updatedAtUtc',
      minWidth: 160,
      render(row) {
        return h('span', { class: 'cell-muted' }, formatDateTime(row.updatedAtUtc));
      },
    },
    {
      title: '操作',
      key: 'actions',
      width: 210,
      align: 'right',
      render(row) {
        if (!options.canManage()) {
          return h('span', { class: 'cell-muted' }, shortId(row.id));
        }

        return h('div', { class: 'row-actions' }, [
          h(UiButton, { size: 'tiny', type: 'info', secondary: true, onClick: () => options.onEdit(row) }, { default: () => '编辑' }),
          h(UiButton, { size: 'tiny', type: row.enabled ? 'warning' : 'success', secondary: true, onClick: () => options.onToggle(row) }, { default: () => (row.enabled ? '禁用' : '启用') }),
          h(UiButton, { size: 'tiny', type: 'error', secondary: true, onClick: () => options.onRemove(row) }, { default: () => '删除' }),
        ]);
      },
    },
  ];
}
