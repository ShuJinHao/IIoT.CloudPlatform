import { mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, nextTick, reactive } from 'vue';
import { Permissions } from '../../types/permissions';
import type { EmployeeListItemDto } from './api';
import { createEmployeeColumns } from './columns';
import EmployeeEditModal from './EmployeeEditModal.vue';
import { employeeRoutes } from './routes';
import { isResetPasswordInvalid } from './types';
import { useEmployees } from './useEmployees';

const employeeApiMocks = vi.hoisted(() => ({
  getEmployeePagedListApi: vi.fn(),
  getEmployeeDetailApi: vi.fn(),
  getEmployeeAccessApi: vi.fn(),
  onboardEmployeeApi: vi.fn(),
  updateEmployeeProfileApi: vi.fn(),
  updateEmployeeAccessApi: vi.fn(),
  deactivateEmployeeApi: vi.fn(),
  terminateEmployeeApi: vi.fn(),
  getAllRolesApi: vi.fn(),
}));

vi.mock('./api', () => employeeApiMocks);

const deviceApiMocks = vi.hoisted(() => ({
  getAllActiveDevicesApi: vi.fn(),
}));

vi.mock('../devices/api', () => deviceApiMocks);

const identityApiMocks = vi.hoisted(() => ({
  getAllDefinedPermissionsApi: vi.fn(),
  getUserPersonalPermissionsApi: vi.fn(),
  updateUserPermissionsApi: vi.fn(),
  resetPasswordApi: vi.fn(),
}));

vi.mock('../../api/identity', () => identityApiMocks);

const feedbackMocks = vi.hoisted(() => ({
  notifySuccess: vi.fn(),
  notifyWarning: vi.fn(),
}));

vi.mock('../../utils/feedback', () => feedbackMocks);

const authMock = vi.hoisted(() => ({
  isAdmin: false,
  permissions: [] as string[],
}));

vi.mock('../../stores/auth', () => ({
  useAuthStore: () => ({
    get isAdmin() {
      return authMock.isAdmin;
    },
    get permissions() {
      return authMock.permissions;
    },
    hasPermission: (permission: string) =>
      authMock.isAdmin || authMock.permissions.includes(permission),
  }),
}));

const employee: EmployeeListItemDto = {
  id: 'employee-1',
  employeeNo: 'E0001',
  realName: '张三',
  isActive: true,
  deviceCount: 2,
};

function emptyEmployeePage() {
  return {
    items: [],
    metaData: {
      totalCount: 0,
      pageSize: 10,
      currentPage: 1,
      totalPages: 1,
    },
  };
}

function mountEmployeeActions(canManagePersonalPermissions: () => boolean) {
  const actionColumn = createEmployeeColumns({
    canUpdateEmployee: () => false,
    canUpdateAccess: () => false,
    canDeactivateEmployee: () => false,
    canTerminateEmployee: () => false,
    canManagePersonalPermissions,
    onDetail: vi.fn(),
    onEdit: vi.fn(),
    onResetPassword: vi.fn(),
    onAccess: vi.fn(),
    onPersonalPermissions: vi.fn(),
    onDeactivate: vi.fn(),
    onTerminate: vi.fn(),
  }).find((column) => column.key === 'actions');

  expect(actionColumn?.render, '员工操作列必须提供 render').toBeTypeOf('function');
  const Harness = defineComponent({
    setup() {
      return () => h('div', [actionColumn!.render!(employee, 0)]);
    },
  });
  return mount(Harness);
}

describe('employees feature guards', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
    authMock.isAdmin = false;
    authMock.permissions = [];
    employeeApiMocks.getEmployeePagedListApi.mockResolvedValue(emptyEmployeePage());
    employeeApiMocks.getEmployeeDetailApi.mockResolvedValue({
      ...employee,
      deviceIds: [],
      roleNames: [],
    });
    employeeApiMocks.getEmployeeAccessApi.mockResolvedValue({ deviceIds: [] });
    employeeApiMocks.getAllRolesApi.mockResolvedValue([]);
    employeeApiMocks.updateEmployeeProfileApi.mockResolvedValue(true);
    deviceApiMocks.getAllActiveDevicesApi.mockResolvedValue([]);
    identityApiMocks.getAllDefinedPermissionsApi.mockResolvedValue([]);
    identityApiMocks.getUserPersonalPermissionsApi.mockResolvedValue([]);
    identityApiMocks.updateUserPermissionsApi.mockResolvedValue(true);
  });

  it('requires employee read permission on the route', () => {
    const route = employeeRoutes[0];
    expect(route).toBeDefined();
    expect(route!.path).toBe('employees');
    expect(route!.meta?.requiredPermission).toBe(Permissions.Employee.Read);
  });

  it('validates reset password confirmation', () => {
    expect(isResetPasswordInvalid('', '')).toBe('请输入新密码');
    expect(isResetPasswordInvalid('Password1', 'Password2')).toBe('两次输入的密码不一致');
    expect(isResetPasswordInvalid('Password1', 'Password1')).toBeNull();
  });

  it('submits only employeeId and realName when updating an employee profile', async () => {
    authMock.permissions = [Permissions.Employee.Update];
    const state = useEmployees();

    state.openEditModal(employee);
    state.editForm.RealName = '张三（新）';
    await state.submitEdit();

    expect(employeeApiMocks.updateEmployeeProfileApi).toHaveBeenCalledTimes(1);
    expect(employeeApiMocks.updateEmployeeProfileApi).toHaveBeenCalledWith(employee.id, {
      employeeId: employee.id,
      realName: '张三（新）',
    });
    const payload = employeeApiMocks.updateEmployeeProfileApi.mock.calls[0]![1];
    expect(payload).not.toHaveProperty('isActive');
    expect(payload).not.toHaveProperty('roleName');
    expect(employeeApiMocks.getEmployeeDetailApi).not.toHaveBeenCalled();
    expect(employeeApiMocks.getAllRolesApi).not.toHaveBeenCalled();
  });

  it('renders no account-status or role controls in the employee edit modal', async () => {
    const Harness = defineComponent({
      components: { EmployeeEditModal },
      setup() {
        const state = reactive({
          show: true,
          form: { RealName: employee.realName },
        });
        return { state, employee };
      },
      template: `
        <EmployeeEditModal
          v-model:show="state.show"
          :form="state.form"
          :target="employee"
          :submitting="false"
        />
      `,
    });

    const wrapper = mount(Harness, { attachTo: document.body });
    await nextTick();

    const modal = document.body.querySelector('.ui-modal');
    expect(modal, '员工编辑弹窗必须渲染').toBeTruthy();
    expect(modal!.textContent).toContain('编辑员工档案');
    expect(modal!.textContent).toContain('工号');
    expect(modal!.textContent).toContain('姓名');
    expect(modal!.textContent).not.toContain('账号状态');
    expect(modal!.textContent).not.toContain('系统角色');
    expect(modal!.querySelectorAll('input')).toHaveLength(2);

    wrapper.unmount();
  });

  it('hides and blocks personal permissions for non-Admin even with access and role permissions', async () => {
    authMock.permissions = [
      Permissions.Employee.UpdateAccess,
      Permissions.Role.Define,
    ];
    const state = useEmployees();
    const actions = mountEmployeeActions(() => state.canManagePersonalPermissions.value);

    expect(state.canManagePersonalPermissions.value).toBe(false);
    expect(actions.text()).not.toContain('特批权限');

    await state.openPersonalPermModal(employee);
    state.togglePersonalPerm(Permissions.Device.Read, true);
    await state.submitPersonalPerm();

    expect(state.showPersonalPermModal.value).toBe(false);
    expect(state.personalPermForm.value).toEqual([]);
    expect(identityApiMocks.getAllDefinedPermissionsApi).not.toHaveBeenCalled();
    expect(identityApiMocks.getUserPersonalPermissionsApi).not.toHaveBeenCalled();
    expect(identityApiMocks.updateUserPermissionsApi).not.toHaveBeenCalled();
  });

  it('keeps personal permissions available to a real Admin', async () => {
    authMock.isAdmin = true;
    identityApiMocks.getAllDefinedPermissionsApi.mockResolvedValue([
      {
        groupName: 'Device',
        permissions: [Permissions.Device.Read],
      },
    ]);
    identityApiMocks.getUserPersonalPermissionsApi.mockResolvedValue([
      Permissions.Device.Read,
    ]);
    const state = useEmployees();
    const actions = mountEmployeeActions(() => state.canManagePersonalPermissions.value);

    expect(state.canManagePersonalPermissions.value).toBe(true);
    expect(actions.text()).toContain('特批权限');

    await state.openPersonalPermModal(employee);
    expect(identityApiMocks.getAllDefinedPermissionsApi).toHaveBeenCalledTimes(1);
    expect(identityApiMocks.getUserPersonalPermissionsApi).toHaveBeenCalledWith(employee.id);
    expect(state.personalPermForm.value).toEqual([Permissions.Device.Read]);

    await state.submitPersonalPerm();
    expect(identityApiMocks.updateUserPermissionsApi).toHaveBeenCalledWith(employee.id, {
      userId: employee.id,
      permissions: [Permissions.Device.Read],
    });
  });
});
