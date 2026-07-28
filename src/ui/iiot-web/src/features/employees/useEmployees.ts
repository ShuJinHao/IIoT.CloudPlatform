import { computed, reactive, ref, watch } from 'vue';
import {
  getEmployeeAccessDeviceCandidatesApi,
  type EmployeeAccessDeviceCandidateDto,
} from '../devices/api';
import { useListPage } from '../../core/list-page';
import type { PagedMetaData } from '../../core/types/pagination';
import {
  getAllDefinedPermissionsApi,
  getUserPersonalPermissionsApi,
  resetPasswordApi,
  updateUserPermissionsApi,
  type PermissionGroupDto,
} from '../../api/identity';
import { useAuthStore } from '../../stores/auth';
import { Permissions } from '../../types/permissions';
import { notifySuccess, notifyWarning } from '../../utils/feedback';
import {
  activateEmployeeApi,
  deactivateEmployeeApi,
  getAllRolesApi,
  getEmployeeAccessApi,
  getEmployeeDetailApi,
  getEmployeePagedListApi,
  onboardEmployeeApi,
  terminateEmployeeApi,
  updateEmployeeAccessApi,
  updateEmployeeProfileApi,
  type EmployeeDetailDto,
  type EmployeeListItemDto,
  type UpdateProfilePayload,
} from './api';
import { isResetPasswordInvalid, type EmployeeConfirmDialogState } from './types';

const PAGE_SIZE = 10;
const ADMIN_PERMISSION_EXPIRED_MESSAGE = '管理员权限已失效，请重新登录后重试';
const ACCESS_PERMISSION_EXPIRED_MESSAGE = '设备管辖权权限已失效，请重新登录后重试';
const ACCESS_NOT_READY_MESSAGE = '设备管辖权尚未加载完成，请稍后重试';
const ACCESS_SUBMITTING_MESSAGE = '设备管辖权正在保存，请稍后重试';
const STATUS_PERMISSION_EXPIRED_MESSAGE = '人员状态操作权限已失效，请重新登录后重试';
const STATUS_SUBMITTING_MESSAGE = '人员状态操作正在处理中，请稍后重试';

const emptyMetaData = (): PagedMetaData => ({
  totalCount: 0,
  pageSize: PAGE_SIZE,
  currentPage: 1,
  totalPages: 1,
});

export function useEmployees() {
  const authStore = useAuthStore();
  const metaData = ref<PagedMetaData>(emptyMetaData());
  const availableRoles = ref<string[]>([]);
  const submitting = ref(false);
  const allDevices = ref<EmployeeAccessDeviceCandidateDto[]>([]);
  const showOnboardModal = ref(false);
  const showEditModal = ref(false);
  const showAccessModal = ref(false);
  const showDetailModal = ref(false);
  const showResetPwdModal = ref(false);
  const showPersonalPermModal = ref(false);
  const accessLoading = ref(false);
  const accessReady = ref(false);
  const accessSubmitting = ref(false);
  const confirmSubmitting = ref(false);
  const detailData = ref<EmployeeDetailDto | null>(null);
  const editTarget = ref<EmployeeListItemDto | null>(null);
  const resetPwdTarget = ref<EmployeeListItemDto | null>(null);
  const personalPermTarget = ref<EmployeeListItemDto | null>(null);
  const personalPermLoading = ref(false);
  const personalPermForm = ref<string[]>([]);
  const permissionGroups = ref<PermissionGroupDto[]>([]);
  const accessTargetId = ref('');
  const onboardForm = reactive({ EmployeeNo: '', RealName: '', Password: '', RoleName: null as string | null });
  const editForm = reactive({ RealName: '' });
  const accessForm = reactive({ DeviceIds: [] as string[] });
  const resetPwdForm = reactive({ newPwd: '', confirm: '' });
  const confirmDialog = reactive<EmployeeConfirmDialogState>({
    show: false,
    title: '',
    desc: '',
    confirmText: '',
    confirmType: 'warning',
    onConfirm: async () => {},
  });
  const listPage = useListPage<EmployeeListItemDto, { keyword: string }>({
    initialFilter: { keyword: '' },
    initialPageSize: PAGE_SIZE,
    immediate: false,
    fetcher: async ({ page, pageSize, filter }) => {
      const response = await getEmployeePagedListApi({
        PaginationParams: { PageNumber: page, PageSize: pageSize },
        Keyword: filter.keyword || undefined,
      });
      metaData.value = response.metaData;
      return { items: response.items, total: response.metaData.totalCount };
    },
  });

  const keyword = computed({
    get: () => listPage.filter.keyword,
    set: (value: string) => {
      listPage.filter.keyword = value;
    },
  });
  const canUpdateEmployee = computed(() => authStore.hasPermission(Permissions.Employee.Update));
  const canUpdateAccess = computed(() => authStore.hasPermission(Permissions.Employee.UpdateAccess));
  const canDeactivateEmployee = computed(() => authStore.hasPermission(Permissions.Employee.Deactivate));
  const canResetPassword = computed(() =>
    authStore.isAdmin
    && authStore.hasPermission(Permissions.Employee.Update),
  );
  const canTerminateEmployee = computed(() =>
    authStore.isAdmin
    && authStore.hasPermission(Permissions.Employee.Terminate),
  );
  const canManagePersonalPermissions = computed(() => authStore.isAdmin);
  const deviceNameMap = computed(() => Object.fromEntries(allDevices.value.map((d) => [d.id, d.deviceName])));
  const roleOptions = computed(() => availableRoles.value.map((r) => ({ label: r, value: r })));

  let searchTimer: ReturnType<typeof setTimeout> | null = null;
  let candidateRequestGeneration = 0;
  let accessRequestGeneration = 0;
  let confirmDialogGeneration = 0;
  const accessSubmissionTargetIds = new Set<string>();

  async function prefetchAccessCandidates() {
    if (!canUpdateAccess.value) {
      candidateRequestGeneration += 1;
      allDevices.value = [];
      return;
    }

    const requestGeneration = ++candidateRequestGeneration;
    try {
      const candidates = await getEmployeeAccessDeviceCandidatesApi();
      if (
        requestGeneration === candidateRequestGeneration
        && canUpdateAccess.value
      ) {
        allDevices.value = candidates;
      }
    } catch {
      if (requestGeneration === candidateRequestGeneration) {
        allDevices.value = [];
      }
    }
  }

  async function fetchList() {
    await listPage.refresh();
    if (listPage.error.value) {
      metaData.value = emptyMetaData();
      listPage.page.value = 1;
    }
  }

  function onSearchInput() {
    if (searchTimer) clearTimeout(searchTimer);
    searchTimer = setTimeout(() => {
      listPage.page.value = 1;
      void fetchList();
    }, 400);
  }

  function onClearKeyword() {
    keyword.value = '';
    listPage.page.value = 1;
    void fetchList();
  }

  function onPageChange(page: number) {
    listPage.gotoPage(page);
  }

  async function refreshAfterMutation() {
    await fetchList();
    if (listPage.items.value.length === 0 && listPage.page.value > 1) {
      listPage.page.value -= 1;
      await fetchList();
    }
  }

  async function initialize() {
    await Promise.all([fetchList(), prefetchAccessCandidates()]);
  }

  async function openOnboardModal() {
    Object.assign(onboardForm, { EmployeeNo: '', RealName: '', Password: '', RoleName: null });
    showOnboardModal.value = true;
    if (!canUpdateAccess.value) {
      availableRoles.value = [];
      return;
    }
    await loadAssignableRoles();
  }

  async function loadAssignableRoles() {
    try {
      const roles = await getAllRolesApi();
      availableRoles.value = roles.filter((r) => r !== 'Admin');
    } catch {
      availableRoles.value = [];
    }
  }

  async function submitOnboard() {
    if (!onboardForm.EmployeeNo.trim() || !onboardForm.RealName.trim() || !onboardForm.Password.trim()) {
      notifyWarning('工号、姓名和初始密码为必填项');
      return;
    }
    submitting.value = true;
    try {
      await onboardEmployeeApi({
        employeeNo: onboardForm.EmployeeNo,
        realName: onboardForm.RealName,
        password: onboardForm.Password,
        roleName: canUpdateAccess.value ? onboardForm.RoleName || undefined : undefined,
      });
      showOnboardModal.value = false;
      await fetchList();
    } finally {
      submitting.value = false;
    }
  }

  function openEditModal(employee: EmployeeListItemDto) {
    editTarget.value = employee;
    editForm.RealName = employee.realName;
    showEditModal.value = true;
  }

  async function submitEdit() {
    if (!editTarget.value || !editForm.RealName.trim()) {
      notifyWarning('姓名不能为空');
      return;
    }
    submitting.value = true;
    try {
      const payload: UpdateProfilePayload = {
        employeeId: editTarget.value.id,
        realName: editForm.RealName,
      };
      await updateEmployeeProfileApi(editTarget.value.id, payload);
      showEditModal.value = false;
      await fetchList();
    } finally {
      submitting.value = false;
    }
  }

  function isCurrentAccessSession(requestGeneration: number, targetId: string) {
    return (
      requestGeneration === accessRequestGeneration
      && accessTargetId.value === targetId
      && showAccessModal.value
    );
  }

  function closeAccessModal(options: { clearCandidates?: boolean } = {}) {
    accessRequestGeneration += 1;
    accessTargetId.value = '';
    accessReady.value = false;
    accessSubmitting.value = false;
    accessForm.DeviceIds = [];
    accessLoading.value = false;
    if (options.clearCandidates) {
      allDevices.value = [];
    }
    showAccessModal.value = false;
  }

  watch(
    showAccessModal,
    (show) => {
      if (
        !show
        && (
          accessTargetId.value
          || accessLoading.value
          || accessReady.value
          || accessSubmitting.value
          || accessForm.DeviceIds.length > 0
        )
      ) {
        closeAccessModal();
      }
    },
    { flush: 'sync' },
  );

  async function openAccessModal(id: string) {
    if (!canUpdateAccess.value) {
      closeAccessModal();
      notifyWarning(ACCESS_PERMISSION_EXPIRED_MESSAGE);
      return;
    }
    if (accessSubmissionTargetIds.has(id)) {
      notifyWarning(ACCESS_SUBMITTING_MESSAGE);
      return;
    }

    candidateRequestGeneration += 1;
    const requestGeneration = ++accessRequestGeneration;
    accessTargetId.value = id;
    accessForm.DeviceIds = [];
    accessReady.value = false;
    accessSubmitting.value = false;
    accessLoading.value = true;
    showAccessModal.value = true;

    try {
      const [candidates, access] = await Promise.all([
        getEmployeeAccessDeviceCandidatesApi(),
        getEmployeeAccessApi(id),
      ]);
      if (!isCurrentAccessSession(requestGeneration, id)) return;

      allDevices.value = [...candidates];
      accessForm.DeviceIds = [...access.deviceIds];
      accessReady.value = true;
    } catch {
      if (!isCurrentAccessSession(requestGeneration, id)) return;
      closeAccessModal({ clearCandidates: true });
    } finally {
      if (isCurrentAccessSession(requestGeneration, id)) {
        accessLoading.value = false;
      }
    }
  }

  function toggleDeviceAccess(deviceId: string, checked: boolean) {
    if (
      !canUpdateAccess.value
      || !accessReady.value
      || accessLoading.value
      || accessSubmitting.value
    ) {
      return;
    }

    if (checked && !accessForm.DeviceIds.includes(deviceId)) accessForm.DeviceIds.push(deviceId);
    if (!checked) {
      const idx = accessForm.DeviceIds.indexOf(deviceId);
      if (idx > -1) accessForm.DeviceIds.splice(idx, 1);
    }
  }

  async function submitAccess() {
    if (!canUpdateAccess.value) {
      closeAccessModal();
      notifyWarning(ACCESS_PERMISSION_EXPIRED_MESSAGE);
      return;
    }
    if (accessSubmitting.value) return;
    if (
      !accessReady.value
      || accessLoading.value
      || !accessTargetId.value
    ) {
      notifyWarning(ACCESS_NOT_READY_MESSAGE);
      return;
    }

    const requestGeneration = accessRequestGeneration;
    const targetId = accessTargetId.value;
    if (accessSubmissionTargetIds.has(targetId)) return;

    const deviceIds = [...accessForm.DeviceIds];
    accessSubmissionTargetIds.add(targetId);
    accessSubmitting.value = true;
    try {
      await updateEmployeeAccessApi(targetId, {
        employeeId: targetId,
        deviceIds,
      });
      notifySuccess('设备管辖权保存成功');
      if (isCurrentAccessSession(requestGeneration, targetId)) {
        closeAccessModal();
      }
      await refreshAfterMutation();
    } finally {
      accessSubmissionTargetIds.delete(targetId);
      if (isCurrentAccessSession(requestGeneration, targetId)) {
        accessSubmitting.value = false;
      }
    }
  }

  async function openDetailModal(id: string) {
    detailData.value = await getEmployeeDetailApi(id);
    showDetailModal.value = true;
  }

  function openResetPwdModal(employee: EmployeeListItemDto) {
    if (!canResetPassword.value) return;

    resetPwdTarget.value = employee;
    resetPwdForm.newPwd = '';
    resetPwdForm.confirm = '';
    showResetPwdModal.value = true;
  }

  async function submitResetPwd() {
    if (!resetPwdTarget.value) return;
    if (!canResetPassword.value) {
      showResetPwdModal.value = false;
      resetPwdTarget.value = null;
      notifyWarning(ADMIN_PERMISSION_EXPIRED_MESSAGE);
      return;
    }

    const validationMessage = isResetPasswordInvalid(resetPwdForm.newPwd, resetPwdForm.confirm);
    if (validationMessage) {
      notifyWarning(validationMessage);
      return;
    }
    submitting.value = true;
    try {
      await resetPasswordApi({ userId: resetPwdTarget.value.id, newPassword: resetPwdForm.newPwd });
      showResetPwdModal.value = false;
      notifySuccess('密码重置成功');
    } finally {
      submitting.value = false;
    }
  }

  async function openPersonalPermModal(employee: EmployeeListItemDto) {
    if (!canManagePersonalPermissions.value) return;

    personalPermTarget.value = employee;
    personalPermLoading.value = true;
    personalPermForm.value = [];
    showPersonalPermModal.value = true;
    try {
      const [groups, currentPerms] = await Promise.all([
        getAllDefinedPermissionsApi(),
        getUserPersonalPermissionsApi(employee.id),
      ]);
      permissionGroups.value = groups;
      personalPermForm.value = [...currentPerms];
    } catch {
      permissionGroups.value = [];
      personalPermForm.value = [];
    } finally {
      personalPermLoading.value = false;
    }
  }

  function togglePersonalPerm(permission: string, checked: boolean) {
    if (!canManagePersonalPermissions.value) return;

    if (checked && !personalPermForm.value.includes(permission)) personalPermForm.value.push(permission);
    if (!checked) {
      const idx = personalPermForm.value.indexOf(permission);
      if (idx > -1) personalPermForm.value.splice(idx, 1);
    }
  }

  async function submitPersonalPerm() {
    if (!canManagePersonalPermissions.value || !personalPermTarget.value) return;

    submitting.value = true;
    try {
      await updateUserPermissionsApi(personalPermTarget.value.id, {
        userId: personalPermTarget.value.id,
        permissions: personalPermForm.value,
      });
      showPersonalPermModal.value = false;
      notifySuccess('特批权限保存成功，员工重新登录后生效');
    } finally {
      submitting.value = false;
    }
  }

  function canOpenStatusConfirm(employee: EmployeeListItemDto, expectedActive: boolean) {
    if (employee.isActive !== expectedActive) return false;
    if (!canDeactivateEmployee.value) {
      confirmDialog.show = false;
      notifyWarning(STATUS_PERMISSION_EXPIRED_MESSAGE);
      return false;
    }
    if (confirmSubmitting.value) {
      notifyWarning(STATUS_SUBMITTING_MESSAGE);
      return false;
    }
    return true;
  }

  async function submitStatusChange(
    generation: number,
    employee: EmployeeListItemDto,
    operation: 'deactivate' | 'activate',
  ) {
    if (confirmSubmitting.value) return;
    if (!canDeactivateEmployee.value) {
      if (generation === confirmDialogGeneration) {
        confirmDialog.show = false;
      }
      notifyWarning(STATUS_PERMISSION_EXPIRED_MESSAGE);
      return;
    }

    confirmSubmitting.value = true;
    try {
      if (operation === 'deactivate') {
        await deactivateEmployeeApi(employee.id);
      } else {
        await activateEmployeeApi(employee.id);
      }
      await refreshAfterMutation();
      notifySuccess(
        operation === 'deactivate'
          ? '员工停用成功'
          : '员工重新启用成功，请通知该员工重新登录',
      );
      if (generation === confirmDialogGeneration) {
        confirmDialog.show = false;
      }
    } catch {
      /* feedback handled by http client */
    } finally {
      confirmSubmitting.value = false;
    }
  }

  function handleDeactivate(employee: EmployeeListItemDto) {
    if (!canOpenStatusConfirm(employee, true)) return;

    const generation = ++confirmDialogGeneration;
    Object.assign(confirmDialog, {
      show: true,
      title: '停用员工',
      desc: `确定要停用「${employee.realName}（${employee.employeeNo}）」吗？停用后该员工将无法登录，现有 Access Token、Refresh Token 和 OIDC 会话立即失效，档案数据保留。`,
      confirmText: '确认停用',
      confirmType: 'warning',
      onConfirm: () => submitStatusChange(generation, employee, 'deactivate'),
    });
  }

  function handleActivate(employee: EmployeeListItemDto) {
    if (!canOpenStatusConfirm(employee, false)) return;

    const generation = ++confirmDialogGeneration;
    Object.assign(confirmDialog, {
      show: true,
      title: '重新启用员工',
      desc: `确定要重新启用「${employee.realName}（${employee.employeeNo}）」吗？员工将恢复登录资格，但停用前的 Access Token、Refresh Token 和 OIDC 会话不会恢复，必须重新登录。`,
      confirmText: '确认重新启用',
      confirmType: 'success',
      onConfirm: () => submitStatusChange(generation, employee, 'activate'),
    });
  }

  function handleTerminate(employee: EmployeeListItemDto) {
    if (!canTerminateEmployee.value) return;
    if (confirmSubmitting.value) {
      notifyWarning(STATUS_SUBMITTING_MESSAGE);
      return;
    }

    const generation = ++confirmDialogGeneration;
    Object.assign(confirmDialog, {
      show: true,
      title: '员工离职销户（不可撤销）',
      desc: `即将永久删除「${employee.realName}（${employee.employeeNo}）」的所有档案，含身份账号与权限数据，此操作不可撤销！`,
      confirmText: '确认离职销户',
      confirmType: 'error',
      onConfirm: async () => {
        if (confirmSubmitting.value) return;
        if (!canTerminateEmployee.value) {
          if (generation === confirmDialogGeneration) {
            confirmDialog.show = false;
          }
          notifyWarning(ADMIN_PERMISSION_EXPIRED_MESSAGE);
          return;
        }

        confirmSubmitting.value = true;
        try {
          await terminateEmployeeApi(employee.id);
          await refreshAfterMutation();
          notifySuccess('员工离职销户成功');
          if (generation === confirmDialogGeneration) {
            confirmDialog.show = false;
          }
        } catch {
          /* feedback handled by http client */
        } finally {
          confirmSubmitting.value = false;
        }
      },
    });
  }

  return {
    employees: listPage.items,
    loading: listPage.loading,
    keyword,
    currentPage: listPage.page,
    metaData,
    availableRoles,
    submitting,
    canUpdateEmployee,
    canUpdateAccess,
    canDeactivateEmployee,
    canResetPassword,
    canTerminateEmployee,
    canManagePersonalPermissions,
    allDevices,
    deviceNameMap,
    roleOptions,
    showOnboardModal,
    onboardForm,
    showEditModal,
    editForm,
    editTarget,
    showAccessModal,
    accessLoading,
    accessReady,
    accessSubmitting,
    accessForm,
    showDetailModal,
    detailData,
    showResetPwdModal,
    resetPwdTarget,
    resetPwdForm,
    showPersonalPermModal,
    personalPermTarget,
    personalPermLoading,
    personalPermForm,
    permissionGroups,
    confirmDialog,
    confirmSubmitting,
    initialize,
    fetchList,
    onSearchInput,
    onClearKeyword,
    onPageChange,
    openOnboardModal,
    submitOnboard,
    openEditModal,
    submitEdit,
    openAccessModal,
    closeAccessModal,
    toggleDeviceAccess,
    submitAccess,
    openDetailModal,
    openResetPwdModal,
    submitResetPwd,
    openPersonalPermModal,
    togglePersonalPerm,
    submitPersonalPerm,
    handleDeactivate,
    handleActivate,
    handleTerminate,
  };
}
