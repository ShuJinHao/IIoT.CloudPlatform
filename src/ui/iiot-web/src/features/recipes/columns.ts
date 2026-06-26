import { h } from 'vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type { RecipeListItemDto } from './types';
import { isDeviceBoundRecipe } from './types';

interface RecipeColumnOptions {
  processNameMap: () => Record<string, string>;
  deviceNameMap: () => Record<string, string>;
  canUpdateRecipe: () => boolean;
  onDetail: (recipe: RecipeListItemDto) => void;
  onUpgrade: (recipe: RecipeListItemDto) => void;
  onDelete: (recipe: RecipeListItemDto) => void;
}

export function createRecipeColumns(
  options: RecipeColumnOptions,
): UiDataTableColumn<RecipeListItemDto>[] {
  return [
    {
      title: '配方名称',
      key: 'recipeName',
      minWidth: 180,
      render(row) {
        return h('span', { class: 'cell-name' }, row.recipeName);
      },
    },
    {
      title: '版本',
      key: 'version',
      width: 100,
      render(row) {
        return h(UiTag, { size: 'small', bordered: false, type: 'info' }, { default: () => row.version });
      },
    },
    {
      title: '类型',
      key: 'type',
      width: 110,
      render(row) {
        const bound = isDeviceBoundRecipe(row.deviceId);
        return h(UiTag, { size: 'small', bordered: false, type: bound ? 'warning' : 'info' }, {
          default: () => (bound ? '设备配方' : '未绑定设备'),
        });
      },
    },
    {
      title: '状态',
      key: 'status',
      width: 110,
      render(row) {
        const active = row.status === 'Active';
        return h(UiTag, { size: 'small', bordered: false, type: active ? 'success' : 'warning' }, {
          default: () => (active ? '启用中' : '已归档'),
        });
      },
    },
    {
      title: '所属工序',
      key: 'processId',
      minWidth: 160,
      render(row) {
        return h(
          'span',
          { class: 'cell-chip' },
          options.processNameMap()[row.processId] || `${row.processId.substring(0, 8)}...`,
        );
      },
    },
    {
      title: '所属机台',
      key: 'deviceId',
      minWidth: 140,
      render(row) {
        if (!isDeviceBoundRecipe(row.deviceId)) return h('span', { class: 'cell-muted' }, '-');
        return h(
          'span',
          { class: 'cell-chip cell-chip--warn' },
          options.deviceNameMap()[row.deviceId] || `${row.deviceId.substring(0, 8)}...`,
        );
      },
    },
    {
      title: '操作',
      key: 'actions',
      width: 220,
      align: 'right',
      render(row) {
        return h('div', { class: 'row-actions' }, [
          h(UiButton, { size: 'tiny', type: 'primary', secondary: true, onClick: () => options.onDetail(row) }, { default: () => '详情' }),
          row.status === 'Active' && options.canUpdateRecipe()
            ? h(UiButton, { size: 'tiny', type: 'info', secondary: true, onClick: () => options.onUpgrade(row) }, { default: () => '升版' })
            : null,
          options.canUpdateRecipe()
            ? h(UiButton, { size: 'tiny', type: 'error', secondary: true, onClick: () => options.onDelete(row) }, { default: () => '删除' })
            : null,
        ]);
      },
    },
  ];
}
