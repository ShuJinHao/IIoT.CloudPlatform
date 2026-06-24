import { h } from 'vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type { DeviceListItemDto } from './api';

interface DeviceColumnOptions {
  canUpdateDevice: () => boolean;
  canDeleteDevice: () => boolean;
  processLabel: (processId: string) => string;
  onDetail: (device: DeviceListItemDto) => void;
  onEdit: (device: DeviceListItemDto) => void;
  onDelete: (device: DeviceListItemDto) => void;
}

export function createDeviceColumns(
  options: DeviceColumnOptions,
): UiDataTableColumn<DeviceListItemDto>[] {
  return [
    {
      title: '设备名称',
      key: 'deviceName',
      minWidth: 180,
      render(row) {
        return h('span', { class: 'cell-name' }, row.deviceName);
      },
    },
    {
      title: 'Code',
      key: 'code',
      minWidth: 200,
      render(row) {
        return h('div', { class: 'cell-code-wrap' }, [
          h('code', { class: 'cell-code' }, row.code),
        ]);
      },
    },
    {
      title: '状态',
      key: 'status',
      width: 110,
      render() {
        return h(
          UiTag,
          { size: 'small', bordered: false, type: 'success' },
          { default: () => '已启用' },
        );
      },
    },
    {
      title: '所属工序',
      key: 'processId',
      minWidth: 180,
      render(row) {
        return h('span', { class: 'cell-chip' }, options.processLabel(row.processId));
      },
    },
    {
      title: '操作',
      key: 'actions',
      width: 180,
      align: 'right',
      render(row) {
        const actions = [
          h(
            UiButton,
            {
              size: 'tiny',
              type: 'primary',
              secondary: true,
              onClick: () => options.onDetail(row),
            },
            { default: () => '详情' },
          ),
        ];

        if (options.canUpdateDevice()) {
          actions.push(
            h(
              UiButton,
              {
                size: 'tiny',
                type: 'info',
                secondary: true,
                onClick: () => options.onEdit(row),
              },
              { default: () => '编辑' },
            ),
          );
        }

        if (options.canDeleteDevice()) {
          actions.push(
            h(
              UiButton,
              {
                size: 'tiny',
                type: 'error',
                secondary: true,
                onClick: () => options.onDelete(row),
              },
              { default: () => '删除' },
            ),
          );
        }

        return h('div', { class: 'row-actions' }, actions);
      },
    },
  ];
}
