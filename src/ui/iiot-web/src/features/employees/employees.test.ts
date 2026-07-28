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
  state: null as { isAdmin: boolean; permissions: string[] } | null,
  hasPermission: vi.fn(),
}));

vi.mock('../../stores/auth', () => ({
  useAuthStore: () => ({
    get isAdmin() {
      return authMock.state?.isAdmin ?? false;
    },
    get permissions() {
      return authMock.state?.permissions ?? [];
    },
    hasPermission: (permission: string) => authMock.hasPermission(permission),
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

type EmployeeColumnOptions = Parameters<typeof createEmployeeColumns>[0];

function mountEmployeeActions(options: Partial<EmployeeColumnOptions> = {}) {
  const actionColumn = createEmployeeColumns({
    canUpdateEmployee: () => false,
    canUpdateAccess: () => false,
    canDeactivateEmployee: () => false,
    canResetPassword: () => false,
    canTerminateEmployee: () => false,
    canManagePersonalPermissions: () => false,
    onDetail: vi.fn(),
    onEdit: vi.fn(),
    onResetPassword: vi.fn(),
    onAccess: vi.fn(),
    onPersonalPermissions: vi.fn(),
    onDeactivate: vi.fn(),
    onTerminate: vi.fn(),
    ...options,
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
    authMock.state = reactive({
      isAdmin: false,
      permissions: [] as string[],
    });
    authMock.hasPermission.mockImplementation(
      (permission: string) =>
        authMock.state!.isAdmin || authMock.state!.permissions.includes(permission),
    );
    employeeApiMocks.getEmployeePagedListApi.mockResolvedValue(emptyEmployeePage());
    employeeApiMocks.getEmployeeDetailApi.mockResolvedValue({
      ...employee,
      deviceIds: [],
      roleNames: [],
    });
    employeeApiMocks.getEmployeeAccessApi.mockResolvedValue({ deviceIds: [] });
    employeeApiMocks.getAllRolesApi.mockResolvedValue([]);
    employeeApiMocks.updateEmployeeProfileApi.mockResolvedValue(true);
    employeeApiMocks.deactivateEmployeeApi.mockResolvedValue(true);
    employeeApiMocks.terminateEmployeeApi.mockResolvedValue(true);
    deviceApiMocks.getAllActiveDevicesApi.mockResolvedValue([]);
    identityApiMocks.getAllDefinedPermissionsApi.mockResolvedValue([]);
    identityApiMocks.getUserPersonalPermissionsApi.mockResolvedValue([]);
    identityApiMocks.updateUserPermissionsApi.mockResolvedValue(true);
    identityApiMocks.resetPasswordApi.mockResolvedValue(true);
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
    authMock.state!.permissions = [Permissions.Employee.Update];
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

  it('hides and blocks reset password and termination for a non-Admin with historical permissions', async () => {
    authMock.state!.permissions = [
      Permissions.Employee.Update,
      Permissions.Employee.Terminate,
    ];
    const state = useEmployees();
    const actions = mountEmployeeActions({
      canUpdateEmployee: () => state.canUpdateEmployee.value,
      canResetPassword: () => state.canResetPassword.value,
      canTerminateEmployee: () => state.canTerminateEmployee.value,
    });

    expect(state.canUpdateEmployee.value).toBe(true);
    expect(state.canResetPassword.value).toBe(false);
    expect(state.canTerminateEmployee.value).toBe(false);
    expect(actions.text()).toContain('编辑');
    expect(actions.text()).not.toContain('重置密码');
    expect(actions.text()).not.toContain('离职');

    state.openResetPwdModal(employee);
    state.resetPwdForm.newPwd = 'Password1';
    state.resetPwdForm.confirm = 'Password1';
    await state.submitResetPwd();
    state.handleTerminate(employee);
    await state.confirmDialog.onConfirm();

    expect(state.showResetPwdModal.value).toBe(false);
    expect(state.resetPwdTarget.value).toBeNull();
    expect(state.confirmDialog.show).toBe(false);
    expect(identityApiMocks.resetPasswordApi).not.toHaveBeenCalled();
    expect(employeeApiMocks.terminateEmployeeApi).not.toHaveBeenCalled();
  });

  it('keeps both high-risk actions available to an Admin with no raw permissions', async () => {
    authMock.state!.isAdmin = true;
    authMock.state!.permissions = [];
    const state = useEmployees();
    const actions = mountEmployeeActions({
      canResetPassword: () => state.canResetPassword.value,
      canTerminateEmployee: () => state.canTerminateEmployee.value,
    });

    expect(state.canResetPassword.value).toBe(true);
    expect(state.canTerminateEmployee.value).toBe(true);
    expect(actions.text()).toContain('重置密码');
    expect(actions.text()).toContain('离职');
    expect(authMock.hasPermission).toHaveBeenCalledWith(Permissions.Employee.Update);
    expect(authMock.hasPermission).toHaveBeenCalledWith(Permissions.Employee.Terminate);

    state.openResetPwdModal(employee);
    expect(state.showResetPwdModal.value).toBe(true);
    state.resetPwdForm.newPwd = 'Password1';
    state.resetPwdForm.confirm = 'Password1';
    await state.submitResetPwd();

    expect(identityApiMocks.resetPasswordApi).toHaveBeenCalledWith({
      userId: employee.id,
      newPassword: 'Password1',
    });
    expect(state.showResetPwdModal.value).toBe(false);

    state.handleTerminate(employee);
    expect(state.confirmDialog.show).toBe(true);
    await state.confirmDialog.onConfirm();

    expect(employeeApiMocks.terminateEmployeeApi).toHaveBeenCalledWith(employee.id);
    expect(state.confirmDialog.show).toBe(false);
  });

  it('blocks both high-risk submissions when Admin identity is lost after opening', async () => {
    authMock.state!.isAdmin = true;
    const state = useEmployees();

    state.openResetPwdModal(employee);
    state.resetPwdForm.newPwd = 'Password1';
    state.resetPwdForm.confirm = 'Password1';
    authMock.state!.isAdmin = false;
    authMock.state!.permissions = [
      Permissions.Employee.Update,
      Permissions.Employee.Terminate,
    ];
    await nextTick();

    expect(state.canResetPassword.value).toBe(false);
    await state.submitResetPwd();
    expect(identityApiMocks.resetPasswordApi).not.toHaveBeenCalled();
    expect(state.showResetPwdModal.value).toBe(false);
    expect(state.resetPwdTarget.value).toBeNull();
    expect(feedbackMocks.notifyWarning).toHaveBeenLastCalledWith(
      '管理员权限已失效，请重新登录后重试',
    );

    authMock.state!.isAdmin = true;
    await nextTick();
    state.handleTerminate(employee);
    expect(state.confirmDialog.show).toBe(true);

    authMock.state!.isAdmin = false;
    await nextTick();
    expect(state.canTerminateEmployee.value).toBe(false);
    await state.confirmDialog.onConfirm();

    expect(employeeApiMocks.terminateEmployeeApi).not.toHaveBeenCalled();
    expect(state.confirmDialog.show).toBe(false);
    expect(feedbackMocks.notifyWarning).toHaveBeenCalledTimes(2);
    expect(feedbackMocks.notifyWarning).toHaveBeenLastCalledWith(
      '管理员权限已失效，请重新登录后重试',
    );
  });

  it('keeps edit, access and deactivate actions for HrAdmin without exposing Admin-only actions', () => {
    authMock.state!.permissions = [
      Permissions.Employee.Update,
      Permissions.Employee.UpdateAccess,
      Permissions.Employee.Deactivate,
    ];
    const state = useEmployees();
    const actions = mountEmployeeActions({
      canUpdateEmployee: () => state.canUpdateEmployee.value,
      canUpdateAccess: () => state.canUpdateAccess.value,
      canDeactivateEmployee: () => state.canDeactivateEmployee.value,
      canResetPassword: () => state.canResetPassword.value,
      canTerminateEmployee: () => state.canTerminateEmployee.value,
      canManagePersonalPermissions: () => state.canManagePersonalPermissions.value,
    });

    expect(actions.text()).toContain('编辑');
    expect(actions.text()).toContain('管辖权');
    expect(actions.text()).toContain('停用');
    expect(actions.text()).not.toContain('重置密码');
    expect(actions.text()).not.toContain('离职');
    expect(actions.text()).not.toContain('特批权限');
  });

  it('hides and blocks personal permissions for non-Admin even with access and role permissions', async () => {
    authMock.state!.permissions = [
      Permissions.Employee.UpdateAccess,
      Permissions.Role.Define,
    ];
    const state = useEmployees();
    const actions = mountEmployeeActions({
      canManagePersonalPermissions: () => state.canManagePersonalPermissions.value,
    });

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
    authMock.state!.isAdmin = true;
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
    const actions = mountEmployeeActions({
      canManagePersonalPermissions: () => state.canManagePersonalPermissions.value,
    });

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
