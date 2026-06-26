import { computed, reactive, ref } from 'vue';
import { getAllActiveDevicesApi, type DeviceSelectDto } from '../devices/api';
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
  const allDevices = ref<DeviceSelectDto[]>([]);
  const showOnboardModal = ref(false);
  const showEditModal = ref(false);
  const showAccessModal = ref(false);
  const showDetailModal = ref(false);
  const showResetPwdModal = ref(false);
  const showPersonalPermModal = ref(false);
  const accessLoading = ref(false);
  const detailData = ref<EmployeeDetailDto | null>(null);
  const editTarget = ref<EmployeeListItemDto | null>(null);
  const resetPwdTarget = ref<EmployeeListItemDto | null>(null);
  const personalPermTarget = ref<EmployeeListItemDto | null>(null);
  const personalPermLoading = ref(false);
  const personalPermForm = ref<string[]>([]);
  const permissionGroups = ref<PermissionGroupDto[]>([]);
  const accessTargetId = ref('');
  const onboardForm = reactive({ EmployeeNo: '', RealName: '', Password: '', RoleName: null as string | null });
  const editForm = reactive({ RealName: '', IsActive: true, RoleName: null as string | null });
  const accessForm = reactive({ DeviceIds: [] as string[] });
  const resetPwdForm = reactive({ newPwd: '', confirm: '' });
  const confirmDialog = reactive<EmployeeConfirmDialogState>({
    show: false,
    title: '',
    desc: '',
    confirmText: '',
    onConfirm: async () => {},
  });
  const editRoleLoaded = ref(false);
  const editRoleLoadFailed = ref(false);

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
  const canTerminateEmployee = computed(() => authStore.hasPermission(Permissions.Employee.Terminate));
  const canManagePersonalPermissions = computed(() =>
    authStore.hasPermission(Permissions.Employee.UpdateAccess)
    && authStore.hasPermission(Permissions.Role.Define),
  );
  const deviceNameMap = computed(() => Object.fromEntries(allDevices.value.map((d) => [d.id, d.deviceName])));
  const roleOptions = computed(() => availableRoles.value.map((r) => ({ label: r, value: r })));

  let searchTimer: ReturnType<typeof setTimeout> | null = null;

  async function fetchSelectData() {
    try {
      allDevices.value = await getAllActiveDevicesApi();
    } catch {
      allDevices.value = [];
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
    await Promise.all([fetchList(), fetchSelectData()]);
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

  async function openEditModal(employee: EmployeeListItemDto) {
    editTarget.value = employee;
    Object.assign(editForm, { RealName: employee.realName, IsActive: employee.isActive, RoleName: null });
    editRoleLoaded.value = false;
    editRoleLoadFailed.value = false;
    if (!canUpdateAccess.value) {
      availableRoles.value = [];
      showEditModal.value = true;
      return;
    }
    try {
      const [roles, detail] = await Promise.all([getAllRolesApi(), getEmployeeDetailApi(employee.id)]);
      availableRoles.value = roles.filter((r) => r !== 'Admin');
      editForm.RoleName = detail.roleNames.find((role) => role !== 'Admin') ?? null;
      editRoleLoaded.value = true;
    } catch {
      availableRoles.value = [];
      editRoleLoadFailed.value = true;
    }
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
        isActive: editForm.IsActive,
      };
      if (canUpdateAccess.value && editRoleLoaded.value) {
        payload.roleName = editForm.RoleName ?? '';
      } else if (canUpdateAccess.value && editRoleLoadFailed.value) {
        notifyWarning('角色信息未加载成功，本次只保存基础档案');
      }
      await updateEmployeeProfileApi(editTarget.value.id, payload);
      showEditModal.value = false;
      await fetchList();
    } finally {
      submitting.value = false;
    }
  }

  async function openAccessModal(id: string) {
    accessTargetId.value = id;
    accessLoading.value = true;
    showAccessModal.value = true;
    await fetchSelectData();
    try {
      const access = await getEmployeeAccessApi(id);
      accessForm.DeviceIds = [...access.deviceIds];
    } catch {
      accessForm.DeviceIds = [];
    } finally {
      accessLoading.value = false;
    }
  }

  function toggleDeviceAccess(deviceId: string, checked: boolean) {
    if (checked && !accessForm.DeviceIds.includes(deviceId)) accessForm.DeviceIds.push(deviceId);
    if (!checked) {
      const idx = accessForm.DeviceIds.indexOf(deviceId);
      if (idx > -1) accessForm.DeviceIds.splice(idx, 1);
    }
  }

  async function submitAccess() {
    submitting.value = true;
    try {
      await updateEmployeeAccessApi(accessTargetId.value, {
        employeeId: accessTargetId.value,
        deviceIds: accessForm.DeviceIds,
      });
      showAccessModal.value = false;
      await fetchList();
    } finally {
      submitting.value = false;
    }
  }

  async function openDetailModal(id: string) {
    detailData.value = await getEmployeeDetailApi(id);
    showDetailModal.value = true;
  }

  function openResetPwdModal(employee: EmployeeListItemDto) {
    resetPwdTarget.value = employee;
    resetPwdForm.newPwd = '';
    resetPwdForm.confirm = '';
    showResetPwdModal.value = true;
  }

  async function submitResetPwd() {
    if (!resetPwdTarget.value) return;
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
    if (checked && !personalPermForm.value.includes(permission)) personalPermForm.value.push(permission);
    if (!checked) {
      const idx = personalPermForm.value.indexOf(permission);
      if (idx > -1) personalPermForm.value.splice(idx, 1);
    }
  }

  async function submitPersonalPerm() {
    if (!personalPermTarget.value) return;
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

  function handleDeactivate(employee: EmployeeListItemDto) {
    Object.assign(confirmDialog, {
      show: true,
      title: '停用员工',
      desc: `确定要停用「${employee.realName}（${employee.employeeNo}）」吗？停用后该员工将无法登录，档案数据保留。`,
      confirmText: '确认停用',
      onConfirm: async () => {
        submitting.value = true;
        try {
          await deactivateEmployeeApi(employee.id);
          confirmDialog.show = false;
          await refreshAfterMutation();
        } finally {
          submitting.value = false;
        }
      },
    });
  }

  function handleTerminate(employee: EmployeeListItemDto) {
    Object.assign(confirmDialog, {
      show: true,
      title: '员工离职销户（不可撤销）',
      desc: `即将永久删除「${employee.realName}（${employee.employeeNo}）」的所有档案，含身份账号与权限数据，此操作不可撤销！`,
      confirmText: '确认离职销户',
      onConfirm: async () => {
        submitting.value = true;
        try {
          await terminateEmployeeApi(employee.id);
          confirmDialog.show = false;
          await refreshAfterMutation();
        } finally {
          submitting.value = false;
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
    editRoleLoadFailed,
    showAccessModal,
    accessLoading,
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
    toggleDeviceAccess,
    submitAccess,
    openDetailModal,
    openResetPwdModal,
    submitResetPwd,
    openPersonalPermModal,
    togglePersonalPerm,
    submitPersonalPerm,
    handleDeactivate,
    handleTerminate,
  };
}
