import { h } from 'vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type { EmployeeListItemDto } from './api';

interface EmployeeColumnOptions {
  canUpdateEmployee: () => boolean;
  canUpdateAccess: () => boolean;
  canDeactivateEmployee: () => boolean;
  canTerminateEmployee: () => boolean;
  canManagePersonalPermissions: () => boolean;
  onDetail: (id: string) => void;
  onEdit: (employee: EmployeeListItemDto) => void;
  onResetPassword: (employee: EmployeeListItemDto) => void;
  onAccess: (id: string) => void;
  onPersonalPermissions: (employee: EmployeeListItemDto) => void;
  onDeactivate: (employee: EmployeeListItemDto) => void;
  onTerminate: (employee: EmployeeListItemDto) => void;
}

export function createEmployeeColumns(
  options: EmployeeColumnOptions,
): UiDataTableColumn<EmployeeListItemDto>[] {
  return [
    {
      title: '工号',
      key: 'employeeNo',
      width: 140,
      render(row) {
        return h('span', { class: 'cell-emp-no' }, row.employeeNo);
      },
    },
    {
      title: '姓名',
      key: 'realName',
      minWidth: 140,
      render(row) {
        return h('span', { class: 'cell-name' }, row.realName);
      },
    },
    {
      title: '状态',
      key: 'isActive',
      width: 100,
      render(row) {
        return h(UiTag, {
          size: 'small',
          bordered: false,
          type: row.isActive ? 'success' : 'default',
        }, { default: () => (row.isActive ? '在职' : '停用') });
      },
    },
    {
      title: '设备管辖',
      key: 'deviceCount',
      width: 110,
      render(row) {
        return h('span', { class: 'cell-mono cell-count' }, `${row.deviceCount} 台`);
      },
    },
    {
      title: '操作',
      key: 'actions',
      width: 320,
      align: 'right',
      render(row) {
        return h('div', { class: 'row-actions' }, [
          h(UiButton, { size: 'tiny', type: 'primary', secondary: true, onClick: () => options.onDetail(row.id) }, { default: () => '详情' }),
          options.canUpdateEmployee()
            ? h(UiButton, { size: 'tiny', type: 'info', secondary: true, onClick: () => options.onEdit(row) }, { default: () => '编辑' })
            : null,
          options.canUpdateEmployee()
            ? h(UiButton, { size: 'tiny', type: 'warning', secondary: true, onClick: () => options.onResetPassword(row) }, { default: () => '重置密码' })
            : null,
          options.canUpdateAccess()
            ? h(UiButton, { size: 'tiny', type: 'primary', secondary: true, onClick: () => options.onAccess(row.id) }, { default: () => '管辖权' })
            : null,
          options.canManagePersonalPermissions()
            ? h(UiButton, { size: 'tiny', type: 'info', secondary: true, onClick: () => options.onPersonalPermissions(row) }, { default: () => '特批权限' })
            : null,
          row.isActive && options.canDeactivateEmployee()
            ? h(UiButton, { size: 'tiny', type: 'warning', secondary: true, onClick: () => options.onDeactivate(row) }, { default: () => '停用' })
            : null,
          options.canTerminateEmployee()
            ? h(UiButton, { size: 'tiny', type: 'error', secondary: true, onClick: () => options.onTerminate(row) }, { default: () => '离职' })
            : null,
        ]);
      },
    },
  ];
}
