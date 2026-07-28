import { mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, nextTick, reactive } from 'vue';
import { Permissions } from '../../types/permissions';
import UiButton from '../../components/ui/UiButton.vue';
import type { EmployeeListItemDto } from './api';
import { createEmployeeColumns } from './columns';
import EmployeeAccessModal from './EmployeeAccessModal.vue';
import EmployeeConfirmModal from './EmployeeConfirmModal.vue';
import EmployeeEditModal from './EmployeeEditModal.vue';
import EmployeeOnboardModal from './EmployeeOnboardModal.vue';
import EmployeeRoleModal from './EmployeeRoleModal.vue';
import { employeeRoutes } from './routes';
import {
  EMPLOYEE_ROLE_CLEAR_SELECTION,
  employeeRoleSelectionValue,
  isResetPasswordInvalid,
  normalizeAssignableRoleNames,
} from './types';
import { useEmployees } from './useEmployees';

const employeeApiMocks = vi.hoisted(() => ({
  getEmployeePagedListApi: vi.fn(),
  getEmployeeDetailApi: vi.fn(),
  getEmployeeAccessApi: vi.fn(),
  onboardEmployeeApi: vi.fn(),
  updateEmployeeProfileApi: vi.fn(),
  updateEmployeeAccessApi: vi.fn(),
  updateEmployeeRoleApi: vi.fn(),
  deactivateEmployeeApi: vi.fn(),
  activateEmployeeApi: vi.fn(),
  terminateEmployeeApi: vi.fn(),
  getAllRolesApi: vi.fn(),
}));

vi.mock('./api', () => employeeApiMocks);

const deviceApiMocks = vi.hoisted(() => ({
  getEmployeeAccessDeviceCandidatesApi: vi.fn(),
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
  state: null as { isAdmin: boolean; permissions: string[]; userId: string } | null,
  hasPermission: vi.fn(),
  hasAllPermissions: vi.fn(),
}));

vi.mock('../../stores/auth', () => ({
  useAuthStore: () => ({
    get isAdmin() {
      return authMock.state?.isAdmin ?? false;
    },
    get permissions() {
      return authMock.state?.permissions ?? [];
    },
    get userId() {
      return authMock.state?.userId ?? '';
    },
    hasPermission: (permission: string) => authMock.hasPermission(permission),
    hasAllPermissions: (permissions: string[]) => authMock.hasAllPermissions(permissions),
  }),
}));

const employee: EmployeeListItemDto = {
  id: 'employee-1',
  employeeNo: 'E0001',
  realName: '张三',
  isActive: true,
  deviceCount: 2,
};

const inactiveEmployee: EmployeeListItemDto = {
  ...employee,
  id: 'employee-2',
  employeeNo: 'E0002',
  realName: '李四',
  isActive: false,
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

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

type EmployeeColumnOptions = Parameters<typeof createEmployeeColumns>[0];

function mountEmployeeActions(
  options: Partial<EmployeeColumnOptions> = {},
  row: EmployeeListItemDto = employee,
) {
  const actionColumn = createEmployeeColumns({
    canUpdateEmployee: () => false,
    canUpdateAccess: () => false,
    canManageRole: () => false,
    canDeactivateEmployee: () => false,
    canResetPassword: () => false,
    canTerminateEmployee: () => false,
    canManagePersonalPermissions: () => false,
    onDetail: vi.fn(),
    onEdit: vi.fn(),
    onResetPassword: vi.fn(),
    onAccess: vi.fn(),
    onRole: vi.fn(),
    onPersonalPermissions: vi.fn(),
    onDeactivate: vi.fn(),
    onActivate: vi.fn(),
    onTerminate: vi.fn(),
    ...options,
  }).find((column) => column.key === 'actions');

  expect(actionColumn?.render, '员工操作列必须提供 render').toBeTypeOf('function');
  const Harness = defineComponent({
    setup() {
      return () => h('div', [actionColumn!.render!(row, 0)]);
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
      userId: 'current-user',
    });
    authMock.hasPermission.mockImplementation(
      (permission: string) =>
        authMock.state!.isAdmin || authMock.state!.permissions.includes(permission),
    );
    authMock.hasAllPermissions.mockImplementation(
      (permissions: string[]) =>
        authMock.state!.isAdmin
        || permissions.every((permission) => authMock.state!.permissions.includes(permission)),
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
    employeeApiMocks.activateEmployeeApi.mockResolvedValue(true);
    employeeApiMocks.terminateEmployeeApi.mockResolvedValue(true);
    employeeApiMocks.updateEmployeeRoleApi.mockResolvedValue(true);
    deviceApiMocks.getEmployeeAccessDeviceCandidatesApi.mockResolvedValue([]);
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

  it('shows exactly one permission-gated status action for each employee state', () => {
    authMock.state!.permissions = [Permissions.Employee.Deactivate];
    const state = useEmployees();
    const options = {
      canDeactivateEmployee: () => state.canDeactivateEmployee.value,
      onDeactivate: state.handleDeactivate,
      onActivate: state.handleActivate,
    };

    const activeActions = mountEmployeeActions(options, employee);
    const inactiveActions = mountEmployeeActions(options, inactiveEmployee);

    expect(activeActions.text()).toContain('停用');
    expect(activeActions.text()).not.toContain('重新启用');
    expect(inactiveActions.text()).toContain('重新启用');
    expect(inactiveActions.text()).not.toContain('停用');

    activeActions.unmount();
    inactiveActions.unmount();
  });

  it('hides and blocks both status actions without Employee.Deactivate', () => {
    const state = useEmployees();
    const activeActions = mountEmployeeActions({
      canDeactivateEmployee: () => state.canDeactivateEmployee.value,
    }, employee);
    const inactiveActions = mountEmployeeActions({
      canDeactivateEmployee: () => state.canDeactivateEmployee.value,
    }, inactiveEmployee);

    expect(activeActions.text()).not.toContain('停用');
    expect(inactiveActions.text()).not.toContain('重新启用');

    state.handleDeactivate(employee);
    state.handleActivate(inactiveEmployee);

    expect(state.confirmDialog.show).toBe(false);
    expect(employeeApiMocks.deactivateEmployeeApi).not.toHaveBeenCalled();
    expect(employeeApiMocks.activateEmployeeApi).not.toHaveBeenCalled();
    expect(feedbackMocks.notifyWarning).toHaveBeenCalledTimes(2);
    expect(feedbackMocks.notifyWarning).toHaveBeenLastCalledWith(
      '人员状态操作权限已失效，请重新登录后重试',
    );

    activeActions.unmount();
    inactiveActions.unmount();
  });

  it('uses warning, success and danger semantics for employee confirmations', async () => {
    authMock.state!.permissions = [Permissions.Employee.Deactivate];
    const state = useEmployees();

    state.handleDeactivate(employee);
    expect(state.confirmDialog.title).toBe('停用员工');
    expect(state.confirmDialog.confirmType).toBe('warning');
    expect(state.confirmDialog.desc).toContain('现有 Access Token、Refresh Token 和 OIDC 会话立即失效');

    state.handleActivate(inactiveEmployee);
    expect(state.confirmDialog.title).toBe('重新启用员工');
    expect(state.confirmDialog.confirmType).toBe('success');
    expect(state.confirmDialog.desc).toContain('停用前的 Access Token、Refresh Token 和 OIDC 会话不会恢复');
    expect(state.confirmDialog.desc).toContain('必须重新登录');

    authMock.state!.isAdmin = true;
    state.handleTerminate(employee);
    expect(state.confirmDialog.confirmType).toBe('error');

    const Harness = defineComponent({
      components: { EmployeeConfirmModal },
      setup() {
        return { state };
      },
      template: `
        <EmployeeConfirmModal
          v-model:show="state.confirmDialog.show"
          :dialog="state.confirmDialog"
          :submitting="state.confirmSubmitting.value"
        />
      `,
    });
    const wrapper = mount(Harness, { attachTo: document.body });
    await nextTick();

    const buttons = wrapper.findAllComponents(UiButton);
    expect(buttons).toHaveLength(2);
    expect(buttons[1]!.props('type')).toBe('error');

    state.confirmSubmitting.value = true;
    await nextTick();
    await buttons[0]!.trigger('click');
    document.body.querySelector<HTMLButtonElement>('.ui-modal__close')!.click();
    await nextTick();

    expect(state.confirmDialog.show).toBe(true);
    expect(buttons[0]!.props('disabled')).toBe(true);

    state.confirmSubmitting.value = false;
    await nextTick();
    await buttons[0]!.trigger('click');
    expect(state.confirmDialog.show).toBe(false);

    wrapper.unmount();
  });

  it('closes and blocks both status requests when permission expires after opening', async () => {
    authMock.state!.permissions = [Permissions.Employee.Deactivate];
    const state = useEmployees();

    state.handleDeactivate(employee);
    authMock.state!.permissions = [];
    await nextTick();
    await state.confirmDialog.onConfirm();

    expect(state.confirmDialog.show).toBe(false);
    expect(employeeApiMocks.deactivateEmployeeApi).not.toHaveBeenCalled();

    authMock.state!.permissions = [Permissions.Employee.Deactivate];
    await nextTick();
    state.handleActivate(inactiveEmployee);
    authMock.state!.permissions = [];
    await nextTick();
    await state.confirmDialog.onConfirm();

    expect(state.confirmDialog.show).toBe(false);
    expect(employeeApiMocks.activateEmployeeApi).not.toHaveBeenCalled();
    expect(feedbackMocks.notifyWarning).toHaveBeenCalledTimes(2);
    expect(feedbackMocks.notifyWarning).toHaveBeenLastCalledWith(
      '人员状态操作权限已失效，请重新登录后重试',
    );
  });

  it('submits reactivation once and blocks target switching until it settles', async () => {
    authMock.state!.permissions = [Permissions.Employee.Deactivate];
    const activation = deferred<boolean>();
    employeeApiMocks.activateEmployeeApi.mockReturnValue(activation.promise);
    const state = useEmployees();

    state.handleActivate(inactiveEmployee);
    const activationTitle = state.confirmDialog.title;
    const firstSubmission = state.confirmDialog.onConfirm();
    const duplicateSubmission = state.confirmDialog.onConfirm();
    await duplicateSubmission;

    expect(state.confirmSubmitting.value).toBe(true);
    expect(employeeApiMocks.activateEmployeeApi).toHaveBeenCalledTimes(1);
    expect(employeeApiMocks.activateEmployeeApi).toHaveBeenCalledWith(inactiveEmployee.id);

    state.handleDeactivate(employee);
    expect(state.confirmDialog.title).toBe(activationTitle);
    expect(employeeApiMocks.deactivateEmployeeApi).not.toHaveBeenCalled();
    expect(feedbackMocks.notifyWarning).toHaveBeenCalledWith(
      '人员状态操作正在处理中，请稍后重试',
    );

    activation.resolve(true);
    await firstSubmission;

    expect(state.confirmSubmitting.value).toBe(false);
    expect(state.confirmDialog.show).toBe(false);
    expect(employeeApiMocks.getEmployeePagedListApi).toHaveBeenCalledTimes(1);
    expect(feedbackMocks.notifySuccess).toHaveBeenCalledWith(
      '员工重新启用成功，请通知该员工重新登录',
    );
  });

  it('refreshes the server list after deactivation without optimistic local state', async () => {
    authMock.state!.permissions = [Permissions.Employee.Deactivate];
    employeeApiMocks.getEmployeePagedListApi.mockResolvedValue({
      items: [{ ...employee, isActive: false }],
      metaData: {
        totalCount: 1,
        pageSize: 10,
        currentPage: 1,
        totalPages: 1,
      },
    });
    const state = useEmployees();

    state.handleDeactivate(employee);
    await state.confirmDialog.onConfirm();

    expect(employeeApiMocks.deactivateEmployeeApi).toHaveBeenCalledWith(employee.id);
    expect(employee.isActive).toBe(true);
    expect(state.employees.value).toEqual([{ ...employee, isActive: false }]);
    expect(feedbackMocks.notifySuccess).toHaveBeenCalledWith('员工停用成功');
    expect(state.confirmDialog.show).toBe(false);
  });

  it('keeps the confirmation and server state when a status request fails', async () => {
    authMock.state!.permissions = [Permissions.Employee.Deactivate];
    employeeApiMocks.deactivateEmployeeApi.mockRejectedValue(
      new Error('ProblemDetails handled by http client'),
    );
    const state = useEmployees();

    state.handleDeactivate(employee);
    await state.confirmDialog.onConfirm();

    expect(state.confirmDialog.show).toBe(true);
    expect(state.confirmSubmitting.value).toBe(false);
    expect(employee.isActive).toBe(true);
    expect(employeeApiMocks.getEmployeePagedListApi).not.toHaveBeenCalled();
    expect(feedbackMocks.notifySuccess).not.toHaveBeenCalled();
  });

  it('preserves a page-two refresh failure before reporting a completed status write', async () => {
    authMock.state!.permissions = [Permissions.Employee.Deactivate];
    const state = useEmployees();
    state.currentPage.value = 2;
    await vi.waitFor(() => {
      expect(employeeApiMocks.getEmployeePagedListApi).toHaveBeenCalledTimes(1);
    });
    employeeApiMocks.getEmployeePagedListApi.mockClear();
    employeeApiMocks.getEmployeePagedListApi.mockRejectedValueOnce(
      new Error('ProblemDetails handled by http client'),
    );

    state.handleActivate(inactiveEmployee);
    await state.confirmDialog.onConfirm();
    await nextTick();

    expect(employeeApiMocks.activateEmployeeApi).toHaveBeenCalledWith(inactiveEmployee.id);
    expect(employeeApiMocks.getEmployeePagedListApi).toHaveBeenCalledTimes(1);
    expect(feedbackMocks.notifySuccess).not.toHaveBeenCalled();
    expect(feedbackMocks.notifyWarning).toHaveBeenCalledWith(
      '员工操作已完成，但列表刷新失败，请重新加载页面确认最新状态',
    );
    expect(state.currentPage.value).toBe(2);
    expect(state.confirmDialog.show).toBe(false);
    expect(state.confirmSubmitting.value).toBe(false);
  });

  it('does not report full termination success when the list refresh fails', async () => {
    authMock.state!.isAdmin = true;
    employeeApiMocks.getEmployeePagedListApi.mockRejectedValue(
      new Error('ProblemDetails handled by http client'),
    );
    const state = useEmployees();

    state.handleTerminate(employee);
    await state.confirmDialog.onConfirm();

    expect(employeeApiMocks.terminateEmployeeApi).toHaveBeenCalledWith(employee.id);
    expect(employeeApiMocks.getEmployeePagedListApi).toHaveBeenCalledTimes(1);
    expect(feedbackMocks.notifySuccess).not.toHaveBeenCalled();
    expect(feedbackMocks.notifyWarning).toHaveBeenCalledWith(
      '员工操作已完成，但列表刷新失败，请重新加载页面确认最新状态',
    );
    expect(state.confirmDialog.show).toBe(false);
    expect(state.confirmSubmitting.value).toBe(false);
  });

  it('loads the previous page once when termination empties the current page', async () => {
    authMock.state!.isAdmin = true;
    const state = useEmployees();
    state.currentPage.value = 2;
    await vi.waitFor(() => {
      expect(employeeApiMocks.getEmployeePagedListApi).toHaveBeenCalledTimes(1);
    });
    employeeApiMocks.getEmployeePagedListApi.mockReset();
    employeeApiMocks.getEmployeePagedListApi
      .mockResolvedValueOnce({
        items: [],
        metaData: {
          totalCount: 10,
          pageSize: 10,
          currentPage: 2,
          totalPages: 1,
        },
      })
      .mockResolvedValueOnce({
        items: [employee],
        metaData: {
          totalCount: 10,
          pageSize: 10,
          currentPage: 1,
          totalPages: 1,
        },
      });

    state.handleTerminate(employee);
    await state.confirmDialog.onConfirm();
    await nextTick();

    expect(employeeApiMocks.getEmployeePagedListApi).toHaveBeenCalledTimes(2);
    expect(employeeApiMocks.getEmployeePagedListApi).toHaveBeenNthCalledWith(1, {
      PaginationParams: { PageNumber: 2, PageSize: 10 },
      Keyword: undefined,
    });
    expect(employeeApiMocks.getEmployeePagedListApi).toHaveBeenNthCalledWith(2, {
      PaginationParams: { PageNumber: 1, PageSize: 10 },
      Keyword: undefined,
    });
    expect(state.currentPage.value).toBe(1);
    expect(state.employees.value).toEqual([employee]);
    expect(feedbackMocks.notifySuccess).toHaveBeenCalledWith('员工离职销户成功');
  });

  it('prefetches minimal device candidates only for Employee.UpdateAccess holders', async () => {
    const readerState = useEmployees();
    await readerState.initialize();

    expect(deviceApiMocks.getEmployeeAccessDeviceCandidatesApi).not.toHaveBeenCalled();
    expect(readerState.allDevices.value).toEqual([]);

    authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
    deviceApiMocks.getEmployeeAccessDeviceCandidatesApi.mockResolvedValue([
      { id: 'device-1', deviceName: '正极模切客户端' },
      { id: 'device-2', deviceName: '负极模切客户端' },
    ]);
    const hrAdminState = useEmployees();
    await hrAdminState.initialize();

    expect(deviceApiMocks.getEmployeeAccessDeviceCandidatesApi).toHaveBeenCalledTimes(1);
    expect(hrAdminState.allDevices.value).toEqual([
      { id: 'device-1', deviceName: '正极模切客户端' },
      { id: 'device-2', deviceName: '负极模切客户端' },
    ]);
    expect(hrAdminState.allDevices.value[0]).not.toHaveProperty('code');
    expect(hrAdminState.allDevices.value[0]).not.toHaveProperty('processId');
  });

  it('loads device candidates and current access in parallel and commits them atomically', async () => {
    authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
    const candidates = deferred<Array<{ id: string; deviceName: string }>>();
    const currentAccess = deferred<{ deviceIds: string[] }>();
    deviceApiMocks.getEmployeeAccessDeviceCandidatesApi.mockReturnValue(candidates.promise);
    employeeApiMocks.getEmployeeAccessApi.mockReturnValue(currentAccess.promise);
    const state = useEmployees();

    const opening = state.openAccessModal(employee.id);

    expect(deviceApiMocks.getEmployeeAccessDeviceCandidatesApi).toHaveBeenCalledTimes(1);
    expect(employeeApiMocks.getEmployeeAccessApi).toHaveBeenCalledWith(employee.id);
    expect(state.showAccessModal.value).toBe(true);
    expect(state.accessLoading.value).toBe(true);
    expect(state.accessReady.value).toBe(false);
    expect(state.allDevices.value).toEqual([]);
    expect(state.accessForm.DeviceIds).toEqual([]);

    candidates.resolve([
      { id: 'device-1', deviceName: '正极模切客户端' },
    ]);
    await Promise.resolve();

    expect(state.accessReady.value).toBe(false);
    expect(state.allDevices.value).toEqual([]);
    expect(state.accessForm.DeviceIds).toEqual([]);

    currentAccess.resolve({ deviceIds: ['device-1'] });
    await opening;

    expect(state.accessLoading.value).toBe(false);
    expect(state.accessReady.value).toBe(true);
    expect(state.allDevices.value).toEqual([
      { id: 'device-1', deviceName: '正极模切客户端' },
    ]);
    expect(state.accessForm.DeviceIds).toEqual(['device-1']);
  });

  it.each(['candidates', 'current access'] as const)(
    'closes and clears an unready access modal when %s loading fails',
    async (failure) => {
      authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
      deviceApiMocks.getEmployeeAccessDeviceCandidatesApi.mockImplementation(() =>
        failure === 'candidates'
          ? Promise.reject(new Error('candidate failure'))
          : Promise.resolve([{ id: 'device-1', deviceName: '正极模切客户端' }]));
      employeeApiMocks.getEmployeeAccessApi.mockImplementation(() =>
        failure === 'current access'
          ? Promise.reject(new Error('access failure'))
          : Promise.resolve({ deviceIds: ['device-1'] }));
      const state = useEmployees();

      await state.openAccessModal(employee.id);
      await state.submitAccess();

      expect(state.showAccessModal.value).toBe(false);
      expect(state.accessLoading.value).toBe(false);
      expect(state.accessReady.value).toBe(false);
      expect(state.allDevices.value).toEqual([]);
      expect(state.accessForm.DeviceIds).toEqual([]);
      expect(employeeApiMocks.updateEmployeeAccessApi).not.toHaveBeenCalled();
    },
  );

  it('keeps the latest employee state when an earlier employee request arrives late', async () => {
    authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
    const firstCandidates = deferred<Array<{ id: string; deviceName: string }>>();
    const secondCandidates = deferred<Array<{ id: string; deviceName: string }>>();
    const firstAccess = deferred<{ deviceIds: string[] }>();
    const secondAccess = deferred<{ deviceIds: string[] }>();
    deviceApiMocks.getEmployeeAccessDeviceCandidatesApi
      .mockReturnValueOnce(firstCandidates.promise)
      .mockReturnValueOnce(secondCandidates.promise);
    employeeApiMocks.getEmployeeAccessApi
      .mockReturnValueOnce(firstAccess.promise)
      .mockReturnValueOnce(secondAccess.promise);
    const state = useEmployees();

    const firstOpening = state.openAccessModal('employee-1');
    const secondOpening = state.openAccessModal('employee-2');
    secondCandidates.resolve([{ id: 'device-2', deviceName: '负极模切客户端' }]);
    secondAccess.resolve({ deviceIds: ['device-2'] });
    await secondOpening;

    firstCandidates.resolve([{ id: 'device-1', deviceName: '正极模切客户端' }]);
    firstAccess.resolve({ deviceIds: ['device-1'] });
    await firstOpening;

    expect(state.showAccessModal.value).toBe(true);
    expect(state.accessLoading.value).toBe(false);
    expect(state.accessReady.value).toBe(true);
    expect(state.allDevices.value).toEqual([
      { id: 'device-2', deviceName: '负极模切客户端' },
    ]);
    expect(state.accessForm.DeviceIds).toEqual(['device-2']);
  });

  it('does not restore state when a closed access request arrives late', async () => {
    authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
    const candidates = deferred<Array<{ id: string; deviceName: string }>>();
    const currentAccess = deferred<{ deviceIds: string[] }>();
    deviceApiMocks.getEmployeeAccessDeviceCandidatesApi.mockReturnValue(candidates.promise);
    employeeApiMocks.getEmployeeAccessApi.mockReturnValue(currentAccess.promise);
    const state = useEmployees();

    const opening = state.openAccessModal(employee.id);
    state.showAccessModal.value = false;
    await nextTick();
    candidates.resolve([{ id: 'device-1', deviceName: '正极模切客户端' }]);
    currentAccess.resolve({ deviceIds: ['device-1'] });
    await opening;

    expect(state.showAccessModal.value).toBe(false);
    expect(state.accessLoading.value).toBe(false);
    expect(state.accessReady.value).toBe(false);
    expect(state.allDevices.value).toEqual([]);
    expect(state.accessForm.DeviceIds).toEqual([]);
    await state.submitAccess();
    expect(employeeApiMocks.updateEmployeeAccessApi).not.toHaveBeenCalled();
  });

  it('keeps the latest result when the same employee is opened repeatedly', async () => {
    authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
    const firstCandidates = deferred<Array<{ id: string; deviceName: string }>>();
    const secondCandidates = deferred<Array<{ id: string; deviceName: string }>>();
    const firstAccess = deferred<{ deviceIds: string[] }>();
    const secondAccess = deferred<{ deviceIds: string[] }>();
    deviceApiMocks.getEmployeeAccessDeviceCandidatesApi
      .mockReturnValueOnce(firstCandidates.promise)
      .mockReturnValueOnce(secondCandidates.promise);
    employeeApiMocks.getEmployeeAccessApi
      .mockReturnValueOnce(firstAccess.promise)
      .mockReturnValueOnce(secondAccess.promise);
    const state = useEmployees();

    const firstOpening = state.openAccessModal(employee.id);
    const secondOpening = state.openAccessModal(employee.id);
    secondCandidates.resolve([{ id: 'device-2', deviceName: '第二次候选' }]);
    secondAccess.resolve({ deviceIds: ['device-2'] });
    await secondOpening;
    firstCandidates.resolve([{ id: 'device-1', deviceName: '第一次候选' }]);
    firstAccess.resolve({ deviceIds: ['device-1'] });
    await firstOpening;

    expect(state.allDevices.value).toEqual([
      { id: 'device-2', deviceName: '第二次候选' },
    ]);
    expect(state.accessForm.DeviceIds).toEqual(['device-2']);
    expect(state.accessReady.value).toBe(true);
  });

  it('does not request or mutate access without Employee.UpdateAccess', async () => {
    authMock.state!.permissions = [Permissions.Employee.Read];
    const state = useEmployees();

    await state.initialize();
    await state.openAccessModal(employee.id);
    await state.submitAccess();

    expect(deviceApiMocks.getEmployeeAccessDeviceCandidatesApi).not.toHaveBeenCalled();
    expect(employeeApiMocks.getEmployeeAccessApi).not.toHaveBeenCalled();
    expect(employeeApiMocks.updateEmployeeAccessApi).not.toHaveBeenCalled();
    expect(state.showAccessModal.value).toBe(false);
    expect(feedbackMocks.notifyWarning).toHaveBeenCalledTimes(2);
    expect(feedbackMocks.notifyWarning).toHaveBeenLastCalledWith(
      '设备管辖权权限已失效，请重新登录后重试',
    );
  });

  it('keeps access controls disabled until loading succeeds and while submitting', async () => {
    const Harness = defineComponent({
      components: { EmployeeAccessModal },
      setup() {
        const state = reactive({
          show: true,
          form: { DeviceIds: [] as string[] },
          devices: [] as Array<{ id: string; deviceName: string }>,
          loading: false,
          ready: false,
          submitting: false,
          canUpdateAccess: true,
        });
        return { state };
      },
      template: `
        <EmployeeAccessModal
          v-model:show="state.show"
          :form="state.form"
          :devices="state.devices"
          :loading="state.loading"
          :ready="state.ready"
          :submitting="state.submitting"
          :can-update-access="state.canUpdateAccess"
        />
      `,
    });
    const wrapper = mount(Harness, { attachTo: document.body });
    await nextTick();

    let modal = document.body.querySelector('.ui-modal')!;
    let saveButton = Array.from(modal.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('保存管辖权'))!;
    expect(saveButton.disabled).toBe(true);
    expect(modal.textContent).not.toContain('暂无设备数据');

    const harnessState = wrapper.vm.state;
    harnessState.devices = [{ id: 'device-1', deviceName: '正极模切客户端' }];
    harnessState.ready = true;
    harnessState.submitting = true;
    await nextTick();

    modal = document.body.querySelector('.ui-modal')!;
    saveButton = Array.from(modal.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('保存管辖权'))!;
    expect(saveButton.disabled).toBe(true);
    expect(modal.querySelector<HTMLInputElement>('input[type="checkbox"]')?.disabled).toBe(true);

    wrapper.unmount();
  });

  it('blocks unready, permission-expired and duplicate access submissions', async () => {
    authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
    const state = useEmployees();

    await state.submitAccess();
    expect(employeeApiMocks.updateEmployeeAccessApi).not.toHaveBeenCalled();
    expect(feedbackMocks.notifyWarning).toHaveBeenLastCalledWith(
      '设备管辖权尚未加载完成，请稍后重试',
    );

    deviceApiMocks.getEmployeeAccessDeviceCandidatesApi.mockResolvedValue([
      { id: 'device-1', deviceName: '正极模切客户端' },
      { id: 'device-2', deviceName: '负极模切客户端' },
    ]);
    employeeApiMocks.getEmployeeAccessApi.mockResolvedValue({ deviceIds: ['device-1'] });
    const update = deferred<boolean>();
    employeeApiMocks.updateEmployeeAccessApi.mockReturnValue(update.promise);
    await state.openAccessModal(employee.id);
    state.toggleDeviceAccess('device-2', true);

    const firstSubmission = state.submitAccess();
    state.accessForm.DeviceIds.push('late-local-change');
    const duplicateSubmission = state.submitAccess();
    await duplicateSubmission;

    expect(employeeApiMocks.updateEmployeeAccessApi).toHaveBeenCalledTimes(1);
    expect(employeeApiMocks.updateEmployeeAccessApi).toHaveBeenCalledWith(employee.id, {
      employeeId: employee.id,
      deviceIds: ['device-1', 'device-2'],
    });

    update.resolve(true);
    await firstSubmission;

    await state.openAccessModal(employee.id);
    authMock.state!.permissions = [];
    await nextTick();
    await state.submitAccess();

    expect(employeeApiMocks.updateEmployeeAccessApi).toHaveBeenCalledTimes(1);
    expect(state.showAccessModal.value).toBe(false);
    expect(feedbackMocks.notifyWarning).toHaveBeenLastCalledWith(
      '设备管辖权权限已失效，请重新登录后重试',
    );
  });

  it('allows a confirmed empty formal device set after both reads succeed', async () => {
    authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
    deviceApiMocks.getEmployeeAccessDeviceCandidatesApi.mockResolvedValue([]);
    employeeApiMocks.getEmployeeAccessApi.mockResolvedValue({ deviceIds: [] });
    employeeApiMocks.updateEmployeeAccessApi.mockResolvedValue(true);
    const state = useEmployees();

    await state.openAccessModal(employee.id);
    expect(state.accessReady.value).toBe(true);
    expect(state.allDevices.value).toEqual([]);
    expect(state.accessForm.DeviceIds).toEqual([]);
    await state.submitAccess();

    expect(employeeApiMocks.updateEmployeeAccessApi).toHaveBeenCalledWith(employee.id, {
      employeeId: employee.id,
      deviceIds: [],
    });
    expect(feedbackMocks.notifySuccess).toHaveBeenCalledWith('设备管辖权保存成功');
    expect(employeeApiMocks.getEmployeePagedListApi).toHaveBeenCalledTimes(1);
  });

  it('refreshes the list and reloads the server-persisted device set after saving', async () => {
    authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
    let persistedDeviceIds = ['device-1'];
    deviceApiMocks.getEmployeeAccessDeviceCandidatesApi.mockResolvedValue([
      { id: 'device-1', deviceName: '正极模切客户端' },
      { id: 'device-2', deviceName: '负极模切客户端' },
    ]);
    employeeApiMocks.getEmployeeAccessApi.mockImplementation(async () => ({
      deviceIds: [...persistedDeviceIds],
    }));
    employeeApiMocks.updateEmployeeAccessApi.mockImplementation(async (_id, payload) => {
      persistedDeviceIds = [...payload.deviceIds];
      return true;
    });
    const state = useEmployees();

    await state.openAccessModal(employee.id);
    state.toggleDeviceAccess('device-2', true);
    await state.submitAccess();

    expect(employeeApiMocks.updateEmployeeAccessApi).toHaveBeenCalledWith(employee.id, {
      employeeId: employee.id,
      deviceIds: ['device-1', 'device-2'],
    });
    expect(employeeApiMocks.getEmployeePagedListApi).toHaveBeenCalledTimes(1);
    expect(feedbackMocks.notifySuccess).toHaveBeenCalledWith('设备管辖权保存成功');

    await state.openAccessModal(employee.id);
    expect(state.accessForm.DeviceIds).toEqual(['device-1', 'device-2']);
  });

  it('keeps the unified list loading lifecycle active during the post-save refresh', async () => {
    authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
    const refresh = deferred<ReturnType<typeof emptyEmployeePage>>();
    employeeApiMocks.getEmployeePagedListApi.mockReturnValue(refresh.promise);
    const state = useEmployees();

    await state.openAccessModal(employee.id);
    const submission = state.submitAccess();
    await vi.waitFor(() => {
      expect(employeeApiMocks.getEmployeePagedListApi).toHaveBeenCalledTimes(1);
    });

    expect(state.loading.value).toBe(true);

    refresh.resolve(emptyEmployeePage());
    await submission;

    expect(state.loading.value).toBe(false);
    expect(feedbackMocks.notifySuccess).toHaveBeenCalledWith('设备管辖权保存成功');
  });

  it('does not let an old successful submission close a newer employee modal', async () => {
    authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
    deviceApiMocks.getEmployeeAccessDeviceCandidatesApi.mockResolvedValue([
      { id: 'device-1', deviceName: '正极模切客户端' },
      { id: 'device-2', deviceName: '负极模切客户端' },
    ]);
    employeeApiMocks.getEmployeeAccessApi.mockImplementation(async (id: string) => ({
      deviceIds: id === 'employee-1' ? ['device-1'] : ['device-2'],
    }));
    const oldUpdate = deferred<boolean>();
    employeeApiMocks.updateEmployeeAccessApi.mockReturnValue(oldUpdate.promise);
    const state = useEmployees();

    await state.openAccessModal('employee-1');
    const oldSubmission = state.submitAccess();
    state.closeAccessModal();
    await state.openAccessModal('employee-2');

    oldUpdate.resolve(true);
    await oldSubmission;

    expect(state.showAccessModal.value).toBe(true);
    expect(state.accessReady.value).toBe(true);
    expect(state.accessLoading.value).toBe(false);
    expect(state.accessForm.DeviceIds).toEqual(['device-2']);
    expect(employeeApiMocks.updateEmployeeAccessApi).toHaveBeenCalledTimes(1);
    expect(employeeApiMocks.updateEmployeeAccessApi).toHaveBeenCalledWith('employee-1', {
      employeeId: 'employee-1',
      deviceIds: ['device-1'],
    });
  });

  it('keeps the employee submission lock after closing until the pending write settles', async () => {
    authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
    deviceApiMocks.getEmployeeAccessDeviceCandidatesApi.mockResolvedValue([
      { id: 'device-1', deviceName: '正极模切客户端' },
    ]);
    employeeApiMocks.getEmployeeAccessApi.mockResolvedValue({
      deviceIds: ['device-1'],
    });
    const pendingUpdate = deferred<boolean>();
    employeeApiMocks.updateEmployeeAccessApi.mockReturnValue(pendingUpdate.promise);
    const state = useEmployees();

    await state.openAccessModal(employee.id);
    const submission = state.submitAccess();
    state.closeAccessModal();
    await state.openAccessModal(employee.id);
    await state.submitAccess();

    expect(state.showAccessModal.value).toBe(false);
    expect(deviceApiMocks.getEmployeeAccessDeviceCandidatesApi).toHaveBeenCalledTimes(1);
    expect(employeeApiMocks.getEmployeeAccessApi).toHaveBeenCalledTimes(1);
    expect(employeeApiMocks.updateEmployeeAccessApi).toHaveBeenCalledTimes(1);
    expect(feedbackMocks.notifyWarning).toHaveBeenCalledWith(
      '设备管辖权正在保存，请稍后重试',
    );

    pendingUpdate.resolve(true);
    await submission;
    await state.openAccessModal(employee.id);

    expect(state.showAccessModal.value).toBe(true);
    expect(state.accessReady.value).toBe(true);
    expect(deviceApiMocks.getEmployeeAccessDeviceCandidatesApi).toHaveBeenCalledTimes(2);
    expect(employeeApiMocks.getEmployeeAccessApi).toHaveBeenCalledTimes(2);
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
    expect(feedbackMocks.notifySuccess).toHaveBeenCalledWith('员工离职销户成功');
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

  it('normalizes assignable roles and requires both access-update and role-read for onboarding', async () => {
    expect(normalizeAssignableRoleNames([
      ' Admin ',
      'ADMIN',
      '',
      ' RoleAdmin ',
      'roleadmin',
      ' HrAdmin ',
    ])).toEqual(['RoleAdmin', 'HrAdmin']);

    authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
    const state = useEmployees();
    await state.openOnboardModal();

    expect(state.canManageEmployeeRole.value).toBe(false);
    expect(employeeApiMocks.getAllRolesApi).not.toHaveBeenCalled();
    expect(state.roleOptions.value).toEqual([]);

    authMock.state!.permissions = [
      Permissions.Employee.UpdateAccess,
      Permissions.Role.Read,
    ];
    employeeApiMocks.getAllRolesApi.mockResolvedValue([
      ' Admin ',
      ' RoleAdmin ',
      'roleadmin',
      'HrAdmin',
      ' ',
    ]);
    await state.openOnboardModal();

    expect(state.canManageEmployeeRole.value).toBe(true);
    expect(employeeApiMocks.getAllRolesApi).toHaveBeenCalledTimes(1);
    expect(state.roleOptions.value).toEqual([
      { label: 'RoleAdmin', value: 'RoleAdmin' },
      { label: 'HrAdmin', value: 'HrAdmin' },
    ]);

    const Harness = defineComponent({
      components: { EmployeeOnboardModal },
      setup() {
        const modalState = reactive({
          show: true,
          canAssignRole: false,
          form: {
            EmployeeNo: '',
            RealName: '',
            Password: '',
            RoleName: null as string | null,
          },
        });
        return { modalState, options: state.roleOptions };
      },
      template: `
        <EmployeeOnboardModal
          v-model:show="modalState.show"
          :form="modalState.form"
          :role-options="options"
          :can-assign-role="modalState.canAssignRole"
          :submitting="false"
        />
      `,
    });
    const wrapper = mount(Harness, { attachTo: document.body });
    await nextTick();
    expect(document.body.textContent).not.toContain('系统角色');

    wrapper.vm.modalState.canAssignRole = true;
    await nextTick();
    expect(document.body.textContent).toContain('系统角色');
    wrapper.unmount();
  });

  it('shows the role action only with the dual permission model and hides the current user', () => {
    authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
    const state = useEmployees();
    const options = {
      canManageRole: state.canManageRoleForEmployee,
      onRole: state.openRoleModal,
    };

    let actions = mountEmployeeActions(options, employee);
    expect(actions.text()).not.toContain('角色');
    actions.unmount();

    authMock.state!.permissions = [
      Permissions.Employee.UpdateAccess,
      Permissions.Role.Read,
    ];
    actions = mountEmployeeActions(options, employee);
    expect(actions.text()).toContain('角色');
    actions.unmount();

    authMock.state!.userId = employee.id;
    actions = mountEmployeeActions(options, employee);
    expect(actions.text()).not.toContain('角色');
    actions.unmount();

    authMock.state!.isAdmin = true;
    authMock.state!.permissions = [];
    actions = mountEmployeeActions(options, inactiveEmployee);
    expect(actions.text()).toContain('角色');
    expect(authMock.hasAllPermissions).toHaveBeenCalledWith([
      Permissions.Employee.UpdateAccess,
      Permissions.Role.Read,
    ]);
    actions.unmount();
  });

  it('rejects an Admin-like target after detail loading before role controls become ready', async () => {
    authMock.state!.permissions = [
      Permissions.Employee.UpdateAccess,
      Permissions.Role.Read,
    ];
    employeeApiMocks.getAllRolesApi.mockResolvedValue(['RoleAdmin', 'HrAdmin']);
    employeeApiMocks.getEmployeeDetailApi.mockResolvedValue({
      ...employee,
      deviceIds: [],
      roleNames: [' admin '],
    });
    const state = useEmployees();

    await state.openRoleModal(employee);
    await state.submitRole();

    expect(state.showRoleModal.value).toBe(false);
    expect(state.roleReady.value).toBe(false);
    expect(state.roleTarget.value).toBeNull();
    expect(employeeApiMocks.updateEmployeeRoleApi).not.toHaveBeenCalled();
    expect(feedbackMocks.notifyWarning).toHaveBeenCalledWith(
      'Admin 对应人员禁止通过员工角色入口修改',
    );
  });

  it('initializes zero and single roles, while requiring an explicit choice for legacy role states', async () => {
    authMock.state!.permissions = [
      Permissions.Employee.UpdateAccess,
      Permissions.Role.Read,
    ];
    employeeApiMocks.getAllRolesApi.mockResolvedValue([
      ' Admin ',
      'RoleAdmin',
      ' roleadmin ',
      'HrAdmin',
    ]);
    const state = useEmployees();

    employeeApiMocks.getEmployeeDetailApi.mockResolvedValue({
      ...employee,
      deviceIds: [],
      roleNames: [],
    });
    await state.openRoleModal(employee);
    expect(state.roleForm.Selection).toBe(EMPLOYEE_ROLE_CLEAR_SELECTION);
    expect(state.canSubmitRole.value).toBe(false);
    state.closeRoleModal();

    employeeApiMocks.getEmployeeDetailApi.mockResolvedValue({
      ...employee,
      deviceIds: [],
      roleNames: [' roleadmin '],
    });
    await state.openRoleModal(employee);
    expect(state.roleForm.Selection).toBe(employeeRoleSelectionValue('RoleAdmin'));
    expect(state.canSubmitRole.value).toBe(false);
    expect(state.missingRoleNames.value).toEqual([]);
    state.closeRoleModal();

    employeeApiMocks.getEmployeeDetailApi.mockResolvedValue({
      ...employee,
      deviceIds: [],
      roleNames: ['RoleAdmin', 'HrAdmin'],
    });
    await state.openRoleModal(employee);
    expect(state.roleForm.Selection).toBe('');
    expect(state.hasMultipleCurrentRoles.value).toBe(true);
    expect(state.canSubmitRole.value).toBe(false);
    state.setRoleSelection(EMPLOYEE_ROLE_CLEAR_SELECTION);
    expect(state.canSubmitRole.value).toBe(true);
    state.closeRoleModal();

    employeeApiMocks.getEmployeeDetailApi.mockResolvedValue({
      ...employee,
      deviceIds: [],
      roleNames: ['DeletedLegacyRole'],
    });
    await state.openRoleModal(employee);
    expect(state.roleForm.Selection).toBe('');
    expect(state.missingRoleNames.value).toEqual(['DeletedLegacyRole']);
    expect(state.employeeRoleOptions.value).not.toContainEqual({
      label: 'DeletedLegacyRole',
      value: 'DeletedLegacyRole',
    });
    state.setRoleSelection(employeeRoleSelectionValue('HrAdmin'));
    expect(state.canSubmitRole.value).toBe(true);
  });

  it('keeps a delayed onboarding role load from overwriting the active employee role candidates', async () => {
    authMock.state!.permissions = [
      Permissions.Employee.UpdateAccess,
      Permissions.Role.Read,
    ];
    const onboardingRoles = deferred<string[]>();
    employeeApiMocks.getAllRolesApi
      .mockReturnValueOnce(onboardingRoles.promise)
      .mockResolvedValueOnce(['RoleAdmin']);
    employeeApiMocks.getEmployeeDetailApi.mockResolvedValue({
      ...employee,
      deviceIds: [],
      roleNames: ['RoleAdmin'],
    });
    const state = useEmployees();

    const onboardingOpen = state.openOnboardModal();
    state.showOnboardModal.value = false;
    await state.openRoleModal(employee);
    expect(state.employeeAssignableRoles.value).toEqual(['RoleAdmin']);

    onboardingRoles.resolve(['HrAdmin']);
    await onboardingOpen;

    expect(state.roleOptions.value).toEqual([{ label: 'HrAdmin', value: 'HrAdmin' }]);
    expect(state.employeeAssignableRoles.value).toEqual(['RoleAdmin']);
    expect(state.roleForm.Selection).toBe(employeeRoleSelectionValue('RoleAdmin'));
  });

  it('commits only the latest parallel role load and ignores closed or repeated stale requests', async () => {
    authMock.state!.permissions = [
      Permissions.Employee.UpdateAccess,
      Permissions.Role.Read,
    ];
    const firstRoles = deferred<string[]>();
    const firstDetail = deferred<Awaited<ReturnType<typeof employeeApiMocks.getEmployeeDetailApi>>>();
    const secondRoles = deferred<string[]>();
    const secondDetail = deferred<Awaited<ReturnType<typeof employeeApiMocks.getEmployeeDetailApi>>>();
    employeeApiMocks.getAllRolesApi
      .mockReturnValueOnce(firstRoles.promise)
      .mockReturnValueOnce(secondRoles.promise);
    employeeApiMocks.getEmployeeDetailApi
      .mockReturnValueOnce(firstDetail.promise)
      .mockReturnValueOnce(secondDetail.promise);
    const state = useEmployees();

    const firstOpen = state.openRoleModal(employee);
    const secondOpen = state.openRoleModal(inactiveEmployee);
    expect(employeeApiMocks.getAllRolesApi).toHaveBeenCalledTimes(2);
    expect(employeeApiMocks.getEmployeeDetailApi).toHaveBeenNthCalledWith(1, employee.id);
    expect(employeeApiMocks.getEmployeeDetailApi).toHaveBeenNthCalledWith(2, inactiveEmployee.id);

    firstRoles.resolve(['RoleAdmin']);
    firstDetail.resolve({
      ...employee,
      deviceIds: [],
      roleNames: ['RoleAdmin'],
    });
    await firstOpen;
    expect(state.roleTarget.value?.id).toBe(inactiveEmployee.id);
    expect(state.roleReady.value).toBe(false);

    secondRoles.resolve(['HrAdmin']);
    secondDetail.resolve({
      ...inactiveEmployee,
      deviceIds: [],
      roleNames: ['HrAdmin'],
    });
    await secondOpen;
    expect(state.roleTarget.value?.id).toBe(inactiveEmployee.id);
    expect(state.roleForm.Selection).toBe(employeeRoleSelectionValue('HrAdmin'));
    expect(state.roleReady.value).toBe(true);

    const staleSameRoles = deferred<string[]>();
    const staleSameDetail = deferred<Awaited<ReturnType<typeof employeeApiMocks.getEmployeeDetailApi>>>();
    const latestSameRoles = deferred<string[]>();
    const latestSameDetail = deferred<Awaited<ReturnType<typeof employeeApiMocks.getEmployeeDetailApi>>>();
    employeeApiMocks.getAllRolesApi
      .mockReturnValueOnce(staleSameRoles.promise)
      .mockReturnValueOnce(latestSameRoles.promise);
    employeeApiMocks.getEmployeeDetailApi
      .mockReturnValueOnce(staleSameDetail.promise)
      .mockReturnValueOnce(latestSameDetail.promise);

    const staleSameOpen = state.openRoleModal(inactiveEmployee);
    const latestSameOpen = state.openRoleModal(inactiveEmployee);
    latestSameRoles.resolve(['RoleAdmin']);
    latestSameDetail.resolve({
      ...inactiveEmployee,
      deviceIds: [],
      roleNames: ['RoleAdmin'],
    });
    await latestSameOpen;
    staleSameRoles.resolve(['HrAdmin']);
    staleSameDetail.resolve({
      ...inactiveEmployee,
      deviceIds: [],
      roleNames: ['HrAdmin'],
    });
    await staleSameOpen;

    expect(state.roleForm.Selection).toBe(employeeRoleSelectionValue('RoleAdmin'));
    expect(state.employeeAssignableRoles.value).toEqual(['RoleAdmin']);

    const repeatedRoles = deferred<string[]>();
    const repeatedDetail = deferred<Awaited<ReturnType<typeof employeeApiMocks.getEmployeeDetailApi>>>();
    employeeApiMocks.getAllRolesApi.mockReturnValueOnce(repeatedRoles.promise);
    employeeApiMocks.getEmployeeDetailApi.mockReturnValueOnce(repeatedDetail.promise);
    const repeatedOpen = state.openRoleModal(inactiveEmployee);
    state.showRoleModal.value = false;
    repeatedRoles.resolve(['RoleAdmin']);
    repeatedDetail.resolve({
      ...inactiveEmployee,
      deviceIds: [],
      roleNames: ['RoleAdmin'],
    });
    await repeatedOpen;

    expect(state.showRoleModal.value).toBe(false);
    expect(state.roleTarget.value).toBeNull();
    expect(state.roleDetail.value).toBeNull();
    expect(state.roleForm.Selection).toBe('');
  });

  it('rechecks permission and identity before role submission', async () => {
    authMock.state!.permissions = [
      Permissions.Employee.UpdateAccess,
      Permissions.Role.Read,
    ];
    employeeApiMocks.getAllRolesApi.mockResolvedValue(['RoleAdmin', 'HrAdmin']);
    employeeApiMocks.getEmployeeDetailApi.mockResolvedValue({
      ...employee,
      deviceIds: [],
      roleNames: ['RoleAdmin'],
    });
    const state = useEmployees();

    await state.openRoleModal(employee);
    await state.submitRole();
    expect(employeeApiMocks.updateEmployeeRoleApi).not.toHaveBeenCalled();

    state.setRoleSelection(employeeRoleSelectionValue('HrAdmin'));
    authMock.state!.permissions = [Permissions.Employee.UpdateAccess];
    await nextTick();
    await state.submitRole();
    expect(state.showRoleModal.value).toBe(false);
    expect(employeeApiMocks.updateEmployeeRoleApi).not.toHaveBeenCalled();
    expect(feedbackMocks.notifyWarning).toHaveBeenLastCalledWith(
      '角色管理权限已失效，请重新登录后重试',
    );

    authMock.state!.permissions = [
      Permissions.Employee.UpdateAccess,
      Permissions.Role.Read,
    ];
    await state.openRoleModal(employee);
    state.setRoleSelection(employeeRoleSelectionValue('HrAdmin'));
    authMock.state!.userId = employee.id;
    await state.submitRole();
    expect(state.showRoleModal.value).toBe(false);
    expect(employeeApiMocks.updateEmployeeRoleApi).not.toHaveBeenCalled();
    expect(feedbackMocks.notifyWarning).toHaveBeenLastCalledWith(
      '不能修改当前登录用户自己的角色',
    );
  });

  it('submits canonical replacement and explicit null without a fake list refresh', async () => {
    authMock.state!.permissions = [
      Permissions.Employee.UpdateAccess,
      Permissions.Role.Read,
    ];
    employeeApiMocks.getAllRolesApi.mockResolvedValue(['RoleAdmin', 'HrAdmin']);
    employeeApiMocks.getEmployeeDetailApi.mockResolvedValue({
      ...employee,
      deviceIds: [],
      roleNames: ['RoleAdmin'],
    });
    const state = useEmployees();

    await state.openDetailModal(employee.id);
    await state.openRoleModal(employee);
    state.setRoleSelection(employeeRoleSelectionValue('HrAdmin'));
    await state.submitRole();

    expect(employeeApiMocks.updateEmployeeRoleApi).toHaveBeenCalledWith(employee.id, {
      roleName: 'HrAdmin',
    });
    expect(employeeApiMocks.getEmployeePagedListApi).not.toHaveBeenCalled();
    expect(state.detailData.value).toBeNull();
    expect(state.showRoleModal.value).toBe(false);
    expect(feedbackMocks.notifySuccess).toHaveBeenCalledWith(
      '角色已更新，员工现有会话已失效，请通知员工重新登录',
    );

    employeeApiMocks.updateEmployeeRoleApi.mockClear();
    feedbackMocks.notifySuccess.mockClear();
    await state.openRoleModal(employee);
    state.setRoleSelection(EMPLOYEE_ROLE_CLEAR_SELECTION);
    await state.submitRole();

    expect(employeeApiMocks.updateEmployeeRoleApi).toHaveBeenCalledWith(employee.id, {
      roleName: null,
    });
    expect(feedbackMocks.notifySuccess).toHaveBeenCalledTimes(1);
  });

  it('keeps the modal on PUT failure and locks duplicate or target-switching submissions', async () => {
    authMock.state!.permissions = [
      Permissions.Employee.UpdateAccess,
      Permissions.Role.Read,
    ];
    employeeApiMocks.getAllRolesApi.mockResolvedValue(['RoleAdmin', 'HrAdmin']);
    employeeApiMocks.getEmployeeDetailApi.mockImplementation(async (id: string) => ({
      ...(id === employee.id ? employee : inactiveEmployee),
      deviceIds: [],
      roleNames: ['RoleAdmin'],
    }));
    const state = useEmployees();

    await state.openRoleModal(employee);
    state.setRoleSelection(employeeRoleSelectionValue('HrAdmin'));
    employeeApiMocks.updateEmployeeRoleApi.mockRejectedValueOnce(
      new Error('ProblemDetails handled by http client'),
    );
    await state.submitRole();
    expect(state.showRoleModal.value).toBe(true);
    expect(state.roleSubmitting.value).toBe(false);
    expect(feedbackMocks.notifySuccess).not.toHaveBeenCalled();

    const pendingUpdate = deferred<boolean>();
    employeeApiMocks.updateEmployeeRoleApi.mockReturnValueOnce(pendingUpdate.promise);
    const firstSubmission = state.submitRole();
    const duplicateSubmission = state.submitRole();
    await duplicateSubmission;

    expect(employeeApiMocks.updateEmployeeRoleApi).toHaveBeenCalledTimes(2);
    expect(state.roleSubmitting.value).toBe(true);
    expect(state.closeRoleModal()).toBe(false);
    await state.openRoleModal(inactiveEmployee);
    expect(state.roleTarget.value?.id).toBe(employee.id);
    expect(feedbackMocks.notifyWarning).toHaveBeenCalledWith(
      '员工角色正在保存，请稍后重试',
    );

    pendingUpdate.resolve(true);
    await firstSubmission;
    expect(state.showRoleModal.value).toBe(false);
    expect(state.roleSubmitting.value).toBe(false);
  });

  it('renders legacy and session warnings and blocks modal closing while busy', async () => {
    const Harness = defineComponent({
      components: { EmployeeRoleModal },
      setup() {
        const modalState = reactive({
          show: true,
          submitting: false,
          selection: '',
          closeRequests: 0,
        });
        return {
          modalState,
          employee,
          detail: {
            ...employee,
            deviceIds: [],
            roleNames: ['RoleAdmin', 'DeletedLegacyRole'],
          },
          options: [
            { label: '不分配角色', value: EMPLOYEE_ROLE_CLEAR_SELECTION },
            { label: 'RoleAdmin', value: employeeRoleSelectionValue('RoleAdmin') },
          ],
        };
      },
      template: `
        <EmployeeRoleModal
          :show="modalState.show"
          :target="employee"
          :detail="detail"
          :current-role-names="detail.roleNames"
          :missing-role-names="['DeletedLegacyRole']"
          :role-options="options"
          :selection="modalState.selection"
          :loading="false"
          :ready="true"
          :submitting="modalState.submitting"
          :can-manage-role="true"
          :can-submit="true"
          :has-multiple-roles="true"
          @request-close="modalState.closeRequests += 1"
          @update-selection="modalState.selection = $event"
        />
      `,
    });
    const wrapper = mount(Harness, { attachTo: document.body });
    await nextTick();

    const modal = document.body.querySelector('.ui-modal')!;
    expect(modal.textContent).toContain('检测到多个遗留角色');
    expect(modal.textContent).toContain('DeletedLegacyRole 已不在正式候选清单');
    expect(modal.textContent).toContain('Access Token、Refresh Token 和 OIDC 会话立即失效');
    expect(modal.textContent).toContain('必须重新登录');

    wrapper.vm.modalState.submitting = true;
    await nextTick();
    document.body.querySelector<HTMLButtonElement>('.ui-modal__close')!.click();
    expect(wrapper.vm.modalState.closeRequests).toBe(0);
    expect(document.body.querySelector<HTMLSelectElement>('#employee-role-selection')!.disabled).toBe(true);

    wrapper.vm.modalState.submitting = false;
    await nextTick();
    document.body.querySelector<HTMLButtonElement>('.ui-modal__close')!.click();
    expect(wrapper.vm.modalState.closeRequests).toBe(1);
    wrapper.unmount();
  });
});
