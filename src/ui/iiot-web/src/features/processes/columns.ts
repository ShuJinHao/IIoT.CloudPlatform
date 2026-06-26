import { h } from 'vue';
import UiButton from '../../components/ui/UiButton.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type { ProcessListItemDto } from './api';

interface ProcessColumnOptions {
  canUpdate: () => boolean;
  canDelete: () => boolean;
  onEdit: (process: ProcessListItemDto) => void;
  onDelete: (process: ProcessListItemDto) => void;
}

export function createProcessColumns(
  options: ProcessColumnOptions,
): UiDataTableColumn<ProcessListItemDto>[] {
  return [
    {
      title: '工序编码',
      key: 'processCode',
      width: 180,
      render(row) {
        return h('span', { class: 'cell-code' }, row.processCode);
      },
    },
    {
      title: '工序名称',
      key: 'processName',
      minWidth: 220,
      render(row) {
        return h('span', { class: 'cell-name' }, row.processName);
      },
    },
    {
      title: '工序 ID',
      key: 'id',
      width: 160,
      render(row) {
        return h('span', { class: 'cell-id' }, `${row.id.substring(0, 8)}...`);
      },
    },
    {
      title: '操作',
      key: 'actions',
      width: 140,
      align: 'right',
      render(row) {
        return h('div', { class: 'row-actions' }, [
          options.canUpdate()
            ? h(
                UiButton,
                { size: 'tiny', type: 'primary', secondary: true, onClick: () => options.onEdit(row) },
                { default: () => '编辑' },
              )
            : null,
          options.canDelete()
            ? h(
                UiButton,
                { size: 'tiny', type: 'error', secondary: true, onClick: () => options.onDelete(row) },
                { default: () => '删除' },
              )
            : null,
        ]);
      },
    },
  ];
}
