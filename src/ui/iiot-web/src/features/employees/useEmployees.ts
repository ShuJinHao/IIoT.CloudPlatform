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
  updateEmployeeRoleApi,
  type EmployeeDetailDto,
  type EmployeeListItemDto,
  type UpdateProfilePayload,
} from './api';
import {
  EMPLOYEE_ROLE_CLEAR_SELECTION,
  employeeRoleSelectionValue,
  isAdminLikeRoleName,
  isResetPasswordInvalid,
  normalizeAssignableRoleNames,
  type EmployeeConfirmDialogState,
  type EmployeeRoleForm,
} from './types';

const PAGE_SIZE = 10;
const ADMIN_PERMISSION_EXPIRED_MESSAGE = '管理员权限已失效，请重新登录后重试';
const ACCESS_PERMISSION_EXPIRED_MESSAGE = '设备管辖权权限已失效，请重新登录后重试';
const ACCESS_NOT_READY_MESSAGE = '设备管辖权尚未加载完成，请稍后重试';
const ACCESS_SUBMITTING_MESSAGE = '设备管辖权正在保存，请稍后重试';
const STATUS_PERMISSION_EXPIRED_MESSAGE = '人员状态操作权限已失效，请重新登录后重试';
const STATUS_SUBMITTING_MESSAGE = '人员状态操作正在处理中，请稍后重试';
const ROLE_PERMISSION_EXPIRED_MESSAGE = '角色管理权限已失效，请重新登录后重试';
const ROLE_SELF_UPDATE_MESSAGE = '不能修改当前登录用户自己的角色';
const ROLE_NOT_READY_MESSAGE = '员工角色尚未加载完成，请稍后重试';
const ROLE_SUBMITTING_MESSAGE = '员工角色正在保存，请稍后重试';
const ROLE_TARGET_INVALID_MESSAGE = '员工角色目标已失效，请重新打开后重试';
const ROLE_ADMIN_TARGET_MESSAGE = 'Admin 对应人员禁止通过员工角色入口修改';
const EMPLOYEE_REFRESH_FAILED_MESSAGE = '员工操作已完成，但列表刷新失败，请重新加载页面确认最新状态';

type EmployeePageResponse = Awaited<ReturnType<typeof getEmployeePagedListApi>>;

const emptyMetaData = (): PagedMetaData => ({
  totalCount: 0,
  pageSize: PAGE_SIZE,
  currentPage: 1,
  totalPages: 1,
});

function roleComparisonKey(roleName: string) {
  return roleName.trim().toLowerCase();
}

function normalizeCurrentRoleNames(roleNames: readonly string[]): string[] {
  const normalizedRoles: string[] = [];
  const seen = new Set<string>();

  for (const roleName of roleNames) {
    const normalizedRoleName = roleName.trim();
    const comparisonKey = roleComparisonKey(normalizedRoleName);
    if (!normalizedRoleName || seen.has(comparisonKey)) continue;

    seen.add(comparisonKey);
    normalizedRoles.push(normalizedRoleName);
  }

  return normalizedRoles;
}

export function useEmployees() {
  const authStore = useAuthStore();
  const metaData = ref<PagedMetaData>(emptyMetaData());
  const availableRoles = ref<string[]>([]);
  const employeeAssignableRoles = ref<string[]>([]);
  const submitting = ref(false);
  const allDevices = ref<EmployeeAccessDeviceCandidateDto[]>([]);
  const showOnboardModal = ref(false);
  const showEditModal = ref(false);
  const showAccessModal = ref(false);
  const showRoleModal = ref(false);
  const showDetailModal = ref(false);
  const showResetPwdModal = ref(false);
  const showPersonalPermModal = ref(false);
  const accessLoading = ref(false);
  const accessReady = ref(false);
  const accessSubmitting = ref(false);
  const roleLoading = ref(false);
  const roleReady = ref(false);
  const roleSubmitting = ref(false);
  const confirmSubmitting = ref(false);
  const detailData = ref<EmployeeDetailDto | null>(null);
  const editTarget = ref<EmployeeListItemDto | null>(null);
  const roleTarget = ref<EmployeeListItemDto | null>(null);
  const roleDetail = ref<EmployeeDetailDto | null>(null);
  const resetPwdTarget = ref<EmployeeListItemDto | null>(null);
  const personalPermTarget = ref<EmployeeListItemDto | null>(null);
  const personalPermLoading = ref(false);
  const personalPermForm = ref<string[]>([]);
  const permissionGroups = ref<PermissionGroupDto[]>([]);
  const accessTargetId = ref('');
  const onboardForm = reactive({ EmployeeNo: '', RealName: '', Password: '', RoleName: null as string | null });
  const editForm = reactive({ RealName: '' });
  const accessForm = reactive({ DeviceIds: [] as string[] });
  const roleForm = reactive<EmployeeRoleForm>({ Selection: '' });
  const resetPwdForm = reactive({ newPwd: '', confirm: '' });
  const confirmDialog = reactive<EmployeeConfirmDialogState>({
    show: false,
    title: '',
    desc: '',
    confirmText: '',
    confirmType: 'warning',
    onConfirm: async () => {},
  });
  let queuedEmployeePage: {
    page: number;
    pageSize: number;
    keyword?: string;
    response: EmployeePageResponse;
  } | null = null;
  let fallbackToPreviousEmployeePage = false;
  const listPage = useListPage<EmployeeListItemDto, { keyword: string }>({
    initialFilter: { keyword: '' },
    initialPageSize: PAGE_SIZE,
    immediate: false,
    fetcher: async ({ page, pageSize, filter }) => {
      const requestKeyword = filter.keyword || undefined;
      const queuedPage = queuedEmployeePage;
      const useQueuedPage = queuedPage
        && queuedPage.page === page
        && queuedPage.pageSize === pageSize
        && queuedPage.keyword === requestKeyword;
      if (queuedPage) {
        queuedEmployeePage = null;
      }
      let targetPage = page;
      let response = useQueuedPage
        ? queuedPage.response
        : await getEmployeePagedListApi({
            PaginationParams: { PageNumber: page, PageSize: pageSize },
            Keyword: requestKeyword,
          });
      if (
        !useQueuedPage
        && fallbackToPreviousEmployeePage
        && response.items.length === 0
        && targetPage > 1
      ) {
        targetPage -= 1;
        response = await getEmployeePagedListApi({
          PaginationParams: { PageNumber: targetPage, PageSize: pageSize },
          Keyword: requestKeyword,
        });
      }
      metaData.value = response.metaData;
      if (targetPage !== page) {
        queuedEmployeePage = {
          page: targetPage,
          pageSize,
          keyword: requestKeyword,
          response,
        };
        listPage.page.value = targetPage;
      }
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
  const canManageEmployeeRole = computed(() =>
    authStore.hasAllPermissions([
      Permissions.Employee.UpdateAccess,
      Permissions.Role.Read,
    ]),
  );
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
  const employeeRoleOptions = computed(() => [
    { label: '不分配角色', value: EMPLOYEE_ROLE_CLEAR_SELECTION },
    ...employeeAssignableRoles.value.map((roleName) => ({
      label: roleName,
      value: employeeRoleSelectionValue(roleName),
    })),
  ]);
  const currentRoleNames = computed(() =>
    normalizeCurrentRoleNames(roleDetail.value?.roleNames ?? []),
  );
  const missingRoleNames = computed(() => {
    const candidateKeys = new Set(employeeAssignableRoles.value.map(roleComparisonKey));
    return currentRoleNames.value.filter(
      (roleName) => !candidateKeys.has(roleComparisonKey(roleName)),
    );
  });
  const hasMultipleCurrentRoles = computed(() => currentRoleNames.value.length > 1);
  const selectedCanonicalRoleName = computed(() => {
    if (!roleForm.Selection || roleForm.Selection === EMPLOYEE_ROLE_CLEAR_SELECTION) {
      return null;
    }

    return employeeAssignableRoles.value.find(
      (roleName) => employeeRoleSelectionValue(roleName) === roleForm.Selection,
    ) ?? null;
  });
  const hasRoleChanged = computed(() => {
    if (!roleForm.Selection) return false;
    if (roleForm.Selection === EMPLOYEE_ROLE_CLEAR_SELECTION) {
      return currentRoleNames.value.length > 0;
    }
    if (!selectedCanonicalRoleName.value) return false;

    return currentRoleNames.value.length !== 1
      || roleComparisonKey(currentRoleNames.value[0]!)
        !== roleComparisonKey(selectedCanonicalRoleName.value);
  });
  const canSubmitRole = computed(() =>
    roleReady.value
    && !roleLoading.value
    && !roleSubmitting.value
    && canManageEmployeeRole.value
    && hasRoleChanged.value,
  );

  let searchTimer: ReturnType<typeof setTimeout> | null = null;
  let candidateRequestGeneration = 0;
  let accessRequestGeneration = 0;
  let roleRequestGeneration = 0;
  let confirmDialogGeneration = 0;
  const accessSubmissionTargetIds = new Set<string>();
  const roleSubmissionTargetIds = new Set<string>();

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

  async function refreshAfterMutation(options: { fallbackToPreviousPage?: boolean } = {}) {
    const previousItems = [...listPage.items.value];
    const previousTotal = listPage.total.value;
    const previousMetaData = { ...metaData.value };
    fallbackToPreviousEmployeePage = options.fallbackToPreviousPage === true;
    try {
      await listPage.refresh();
      if (listPage.error.value) {
        listPage.items.value = previousItems;
        listPage.total.value = previousTotal;
        metaData.value = previousMetaData;
        return false;
      }
      return true;
    } finally {
      fallbackToPreviousEmployeePage = false;
    }
  }

  async function initialize() {
    await Promise.all([fetchList(), prefetchAccessCandidates()]);
  }

  async function openOnboardModal() {
    Object.assign(onboardForm, { EmployeeNo: '', RealName: '', Password: '', RoleName: null });
    showOnboardModal.value = true;
    if (!canManageEmployeeRole.value) {
      availableRoles.value = [];
      return;
    }
    await loadAssignableRoles();
  }

  async function loadAssignableRoles() {
    try {
      const roles = await getAllRolesApi();
      availableRoles.value = normalizeAssignableRoleNames(roles);
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
        roleName: canManageEmployeeRole.value ? onboardForm.RoleName || undefined : undefined,
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

  function canManageRoleForEmployee(employee: EmployeeListItemDto) {
    return (
      canManageEmployeeRole.value
      && employee.id !== authStore.userId
    );
  }

  function isCurrentRoleSession(requestGeneration: number, targetId: string) {
    return (
      requestGeneration === roleRequestGeneration
      && roleTarget.value?.id === targetId
      && showRoleModal.value
    );
  }

  function clearRoleModalState() {
    roleRequestGeneration += 1;
    roleTarget.value = null;
    roleDetail.value = null;
    employeeAssignableRoles.value = [];
    roleForm.Selection = '';
    roleReady.value = false;
    roleLoading.value = false;
    roleSubmitting.value = false;
  }

  function closeRoleModal() {
    if (roleLoading.value || roleSubmitting.value) return false;
    showRoleModal.value = false;
    return true;
  }

  watch(
    showRoleModal,
    (show) => {
      if (
        !show
        && (
          roleTarget.value
          || roleDetail.value
          || roleForm.Selection
          || roleLoading.value
          || roleReady.value
          || roleSubmitting.value
        )
      ) {
        clearRoleModalState();
      }
    },
    { flush: 'sync' },
  );

  async function openRoleModal(employee: EmployeeListItemDto) {
    if (!canManageEmployeeRole.value) {
      notifyWarning(ROLE_PERMISSION_EXPIRED_MESSAGE);
      return;
    }
    if (employee.id === authStore.userId) {
      notifyWarning(ROLE_SELF_UPDATE_MESSAGE);
      return;
    }
    if (roleSubmitting.value || roleSubmissionTargetIds.has(employee.id)) {
      notifyWarning(ROLE_SUBMITTING_MESSAGE);
      return;
    }

    const requestGeneration = ++roleRequestGeneration;
    roleTarget.value = employee;
    roleDetail.value = null;
    roleForm.Selection = '';
    roleReady.value = false;
    roleLoading.value = true;
    showRoleModal.value = true;

    try {
      const [roles, detail] = await Promise.all([
        getAllRolesApi(),
        getEmployeeDetailApi(employee.id),
      ]);
      if (!isCurrentRoleSession(requestGeneration, employee.id)) return;
      if (detail.id !== employee.id) {
        notifyWarning(ROLE_TARGET_INVALID_MESSAGE);
        showRoleModal.value = false;
        return;
      }

      const assignableRoles = normalizeAssignableRoleNames(roles);
      const normalizedCurrentRoles = normalizeCurrentRoleNames(detail.roleNames);
      if (normalizedCurrentRoles.some(isAdminLikeRoleName)) {
        notifyWarning(ROLE_ADMIN_TARGET_MESSAGE);
        showRoleModal.value = false;
        return;
      }
      employeeAssignableRoles.value = assignableRoles;
      roleDetail.value = detail;

      if (normalizedCurrentRoles.length === 0) {
        roleForm.Selection = EMPLOYEE_ROLE_CLEAR_SELECTION;
      } else if (normalizedCurrentRoles.length === 1) {
        const currentRoleKey = roleComparisonKey(normalizedCurrentRoles[0]!);
        const currentRoleName = assignableRoles.find(
          (roleName) => roleComparisonKey(roleName) === currentRoleKey,
        );
        roleForm.Selection = currentRoleName
          ? employeeRoleSelectionValue(currentRoleName)
          : '';
      }

      roleReady.value = true;
    } catch {
      if (isCurrentRoleSession(requestGeneration, employee.id)) {
        showRoleModal.value = false;
      }
    } finally {
      if (isCurrentRoleSession(requestGeneration, employee.id)) {
        roleLoading.value = false;
      }
    }
  }

  function setRoleSelection(selection: string) {
    if (
      !canManageEmployeeRole.value
      || !roleReady.value
      || roleLoading.value
      || roleSubmitting.value
    ) {
      return;
    }

    roleForm.Selection = selection;
  }

  async function submitRole() {
    const target = roleTarget.value;
    if (!canManageEmployeeRole.value) {
      showRoleModal.value = false;
      notifyWarning(ROLE_PERMISSION_EXPIRED_MESSAGE);
      return;
    }
    if (!target || target.id === authStore.userId || roleDetail.value?.id !== target.id) {
      showRoleModal.value = false;
      notifyWarning(target?.id === authStore.userId
        ? ROLE_SELF_UPDATE_MESSAGE
        : ROLE_TARGET_INVALID_MESSAGE);
      return;
    }
    if (roleSubmitting.value || roleSubmissionTargetIds.has(target.id)) {
      notifyWarning(ROLE_SUBMITTING_MESSAGE);
      return;
    }
    if (!roleReady.value || roleLoading.value) {
      notifyWarning(ROLE_NOT_READY_MESSAGE);
      return;
    }
    if (!canSubmitRole.value) return;

    const requestedRoleName = roleForm.Selection === EMPLOYEE_ROLE_CLEAR_SELECTION
      ? null
      : selectedCanonicalRoleName.value;
    if (roleForm.Selection !== EMPLOYEE_ROLE_CLEAR_SELECTION && !requestedRoleName) {
      notifyWarning(ROLE_NOT_READY_MESSAGE);
      return;
    }

    const requestGeneration = roleRequestGeneration;
    const targetId = target.id;
    let completed = false;
    roleSubmissionTargetIds.add(targetId);
    roleSubmitting.value = true;
    try {
      await updateEmployeeRoleApi(targetId, { roleName: requestedRoleName });
      completed = true;
    } catch {
      /* feedback handled by http client */
    } finally {
      roleSubmissionTargetIds.delete(targetId);
      if (isCurrentRoleSession(requestGeneration, targetId)) {
        roleSubmitting.value = false;
      }
    }

    if (!completed) return;

    if (detailData.value?.id === targetId) {
      detailData.value = null;
    }
    if (isCurrentRoleSession(requestGeneration, targetId)) {
      showRoleModal.value = false;
    }
    notifySuccess('角色已更新，员工现有会话已失效，请通知员工重新登录');
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
      const refreshed = await refreshAfterMutation();
      if (!refreshed) {
        notifyWarning(EMPLOYEE_REFRESH_FAILED_MESSAGE);
        if (generation === confirmDialogGeneration) {
          confirmDialog.show = false;
        }
        return;
      }
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
          const refreshed = await refreshAfterMutation({ fallbackToPreviousPage: true });
          if (!refreshed) {
            notifyWarning(EMPLOYEE_REFRESH_FAILED_MESSAGE);
            if (generation === confirmDialogGeneration) {
              confirmDialog.show = false;
            }
            return;
          }
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
    employeeAssignableRoles,
    submitting,
    canUpdateEmployee,
    canUpdateAccess,
    canManageEmployeeRole,
    canManageRoleForEmployee,
    canDeactivateEmployee,
    canResetPassword,
    canTerminateEmployee,
    canManagePersonalPermissions,
    allDevices,
    deviceNameMap,
    roleOptions,
    employeeRoleOptions,
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
    showRoleModal,
    roleTarget,
    roleDetail,
    roleLoading,
    roleReady,
    roleSubmitting,
    roleForm,
    currentRoleNames,
    missingRoleNames,
    hasMultipleCurrentRoles,
    canSubmitRole,
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
    openRoleModal,
    closeRoleModal,
    setRoleSelection,
    submitRole,
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
