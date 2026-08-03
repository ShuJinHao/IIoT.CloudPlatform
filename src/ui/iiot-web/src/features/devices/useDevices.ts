import { computed, reactive, ref } from 'vue';
import { useListPage } from '../../core/list-page';
import type { PagedMetaData } from '../../core/types/pagination';
import { useAuthStore } from '../../stores/auth';
import { Permissions } from '../../types/permissions';
import { notifySuccess, notifyWarning } from '../../utils/feedback';
import {
  deleteDeviceApi,
  getDeviceDeletionImpactApi,
  getDeviceLedgerProcessOptionsApi,
  getDevicePagedListApi,
  getDeviceProcessMigrationImpactApi,
  migrateDeviceProcessApi,
  registerDeviceApi,
  updateDeviceProfileApi,
  type DeviceDeletionImpactDto,
  type DeviceLedgerProcessOptionDto,
  type DeviceListItemDto,
} from './api';
import {
  isDeviceDeleteConfirmDisabled,
  type DeviceConfirmDialogState,
  type DeviceDeletionImpactRow,
  type DeviceProcessMigrationDialogState,
} from './types';

const PAGE_SIZE = 10;

const emptyMetaData = (): PagedMetaData => ({
  totalCount: 0,
  pageSize: PAGE_SIZE,
  currentPage: 1,
  totalPages: 1,
});

export function useDevices() {
  const authStore = useAuthStore();
  const submitting = ref(false);
  const metaData = ref<PagedMetaData>(emptyMetaData());
  const allProcesses = ref<DeviceLedgerProcessOptionDto[]>([]);
  const selectedProcessId = ref<string | null>(null);
  const processLoading = ref(false);
  const processError = ref('');
  const showRegisterModal = ref(false);
  const showDetailPanel = ref(false);
  const showEditModal = ref(false);
  const selectedDevice = ref<DeviceListItemDto | null>(null);
  const editTarget = ref<DeviceListItemDto | null>(null);
  const registerForm = reactive({ deviceName: '', processId: null as string | null });
  const editForm = reactive({ deviceName: '' });
  const migrationDialog = reactive<DeviceProcessMigrationDialogState>({
    show: false,
    device: null,
    targetProcessId: null,
    impact: null,
    loading: false,
    error: '',
    confirmInput: '',
  });
  const confirmDialog = reactive<DeviceConfirmDialogState>({
    show: false,
    title: '',
    desc: '',
    confirmText: '',
    danger: true,
    impact: null,
    requiredText: '',
    confirmInput: '',
    onConfirm: async () => {},
  });

  const listPage = useListPage<
    DeviceListItemDto,
    { keyword: string; processId: string | null }
  >({
    initialFilter: { keyword: '', processId: null },
    initialPageSize: PAGE_SIZE,
    immediate: false,
    fetcher: async ({ page, pageSize, filter }) => {
      const response = await getDevicePagedListApi({
        PaginationParams: { PageNumber: page, PageSize: pageSize },
        Keyword: filter.keyword || undefined,
        ProcessId: filter.processId || undefined,
      });
      metaData.value = response.metaData;
      return {
        items: response.items,
        total: response.metaData.totalCount,
      };
    },
  });

  const keyword = computed({
    get: () => listPage.filter.keyword,
    set: (value: string) => {
      listPage.filter.keyword = value;
    },
  });
  const processNameMap = computed(() => {
    const map: Record<string, string> = {};
    for (const p of allProcesses.value) {
      map[p.id] = `${p.processCode} · ${p.processName}`;
    }
    return map;
  });
  const processOptions = computed(() =>
    allProcesses.value.map((p) => ({
      label: `${p.processCode} · ${p.processName}`,
      value: p.id,
    })),
  );
  const canUpdateDevice = computed(() =>
    authStore.hasPermission(Permissions.Device.Update),
  );
  const canDeleteDevice = computed(() =>
    authStore.isAdmin
    && authStore.hasAllPermissions([
      Permissions.Device.Delete,
      Permissions.Device.CascadeDelete,
    ]),
  );
  const canMigrateDevice = computed(() =>
    authStore.isAdmin
    && authStore.hasPermission(Permissions.Device.MigrateProcess),
  );
  const listError = computed(() => listPage.error.value?.message ?? '');
  const deletionImpactRows = computed<DeviceDeletionImpactRow[]>(() => {
    const impact = confirmDialog.impact;
    if (!impact) return [];
    return [
      { label: '配方', value: impact.recipes },
      { label: '产能记录', value: impact.capacities },
      { label: '设备日志', value: impact.deviceLogs },
      { label: '过站数据', value: impact.passStations },
      { label: '客户端状态投影', value: impact.clientStates },
      { label: '客户端版本快照', value: impact.clientVersionSnapshots },
      { label: '插件版本快照', value: impact.clientPluginVersions },
      { label: '运行心跳', value: impact.runtimeHeartbeats },
      { label: '上传幂等登记', value: impact.uploadReceiveRegistrations },
      { label: '人员设备授权', value: impact.employeeDeviceAccesses },
      { label: '设备 refresh token', value: impact.refreshTokenSessions },
      { label: 'PLC 运行状态', value: impact.edgeHostPlcRuntimeStates },
    ];
  });
  const confirmDisabled = computed(() =>
    isDeviceDeleteConfirmDisabled(confirmDialog.requiredText, confirmDialog.confirmInput),
  );

  let searchTimer: ReturnType<typeof setTimeout> | null = null;
  let migrationRequestGeneration = 0;

  async function fetchProcesses() {
    processLoading.value = true;
    processError.value = '';
    try {
      allProcesses.value = await getDeviceLedgerProcessOptionsApi();
    } catch (error) {
      allProcesses.value = [];
      processError.value = errorMessage(error, '工序列表加载失败，请重试。');
    } finally {
      processLoading.value = false;
    }
  }

  async function fetchList() {
    if (!selectedProcessId.value) {
      listPage.clear();
      metaData.value = emptyMetaData();
      return;
    }

    await listPage.refresh();
    if (listPage.error.value) {
      metaData.value = emptyMetaData();
      listPage.page.value = 1;
    }
  }

  async function initialize() {
    selectedProcessId.value = null;
    listPage.filter.processId = null;
    listPage.clear();
    metaData.value = emptyMetaData();
    await fetchProcesses();
  }

  async function selectProcess(value: string | number | boolean | null) {
    const processId = typeof value === 'string'
      && allProcesses.value.some(process => process.id === value)
      ? value
      : null;
    if (selectedProcessId.value === processId) return;

    selectedProcessId.value = processId;
    listPage.filter.processId = processId;
    keyword.value = '';
    listPage.page.value = 1;
    listPage.clear();
    metaData.value = emptyMetaData();
    showDetailPanel.value = false;
    selectedDevice.value = null;
    showEditModal.value = false;
    confirmDialog.show = false;
    closeMigrationDialog();
    if (processId) {
      await fetchList();
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

  function processLabel(processId: string) {
    return processNameMap.value[processId] || `${processId.slice(0, 8)}…`;
  }

  async function refreshAfterMutation() {
    await fetchList();
    if (listPage.items.value.length === 0 && listPage.page.value > 1) {
      listPage.page.value -= 1;
      await fetchList();
    }
  }

  function openDetailPanel(device: DeviceListItemDto) {
    selectedDevice.value = device;
    showDetailPanel.value = true;
  }

  async function openRegisterModal() {
    if (!selectedProcessId.value) {
      notifyWarning('请先选择所属工序。');
      return;
    }
    registerForm.deviceName = '';
    registerForm.processId = selectedProcessId.value;
    showRegisterModal.value = true;
  }

  async function submitRegister() {
    const deviceName = registerForm.deviceName.trim();
    if (!deviceName || !registerForm.processId) {
      notifyWarning('请填写设备名称并选择所属工序。');
      return;
    }
    submitting.value = true;
    try {
      const created = await registerDeviceApi({ deviceName, processId: registerForm.processId });
      showRegisterModal.value = false;
      openDetailPanel({ id: created.id, code: created.code, deviceName, processId: registerForm.processId });
      notifySuccess('设备已创建。请到客户端首装生成页为该设备生成绑定安装包。');
      await fetchList();
    } catch {
      /* feedback handled by http client */
    } finally {
      submitting.value = false;
    }
  }

  function openEditModal(device: DeviceListItemDto) {
    editTarget.value = device;
    editForm.deviceName = device.deviceName;
    showEditModal.value = true;
  }

  async function submitEdit() {
    const deviceName = editForm.deviceName.trim();
    if (!editTarget.value || !deviceName) {
      notifyWarning('设备名称不能为空。');
      return;
    }
    submitting.value = true;
    try {
      await updateDeviceProfileApi(editTarget.value.id, { deviceName });
      if (selectedDevice.value?.id === editTarget.value.id) {
        selectedDevice.value = { ...selectedDevice.value, deviceName };
      }
      showEditModal.value = false;
      await fetchList();
    } catch {
      /* feedback handled by http client */
    } finally {
      submitting.value = false;
    }
  }

  async function handleDelete(device: DeviceListItemDto) {
    if (!canDeleteDevice.value) return;

    submitting.value = true;
    let impact: DeviceDeletionImpactDto;
    try {
      impact = await getDeviceDeletionImpactApi(device.id);
    } catch {
      submitting.value = false;
      return;
    }
    submitting.value = false;

    Object.assign(confirmDialog, {
      show: true,
      danger: true,
      title: '确认级联删除设备',
      desc: '该操作会永久删除设备主数据及下列关联数据，删除后不可恢复。',
      confirmText: '确认级联删除',
      impact,
      requiredText: '',
      confirmInput: '',
      onConfirm: async () => {
        if (!canDeleteDevice.value || confirmDisabled.value) return;

        submitting.value = true;
        try {
          await deleteDeviceApi(device.id);
          if (selectedDevice.value?.id === device.id) {
            showDetailPanel.value = false;
            selectedDevice.value = null;
          }
          confirmDialog.show = false;
          confirmDialog.impact = null;
          await refreshAfterMutation();
        } catch {
          /* feedback handled by http client */
        } finally {
          submitting.value = false;
        }
      },
    });
  }

  function openMigrationDialog(device: DeviceListItemDto) {
    if (!canMigrateDevice.value) return;
    migrationRequestGeneration += 1;
    Object.assign(migrationDialog, {
      show: true,
      device,
      targetProcessId: null,
      impact: null,
      loading: false,
      error: '',
      confirmInput: '',
    });
  }

  function closeMigrationDialog() {
    migrationRequestGeneration += 1;
    Object.assign(migrationDialog, {
      show: false,
      device: null,
      targetProcessId: null,
      impact: null,
      loading: false,
      error: '',
      confirmInput: '',
    });
  }

  async function selectMigrationTarget(targetProcessId: string | null) {
    const device = migrationDialog.device;
    const generation = ++migrationRequestGeneration;
    migrationDialog.targetProcessId = targetProcessId;
    migrationDialog.impact = null;
    migrationDialog.error = '';
    migrationDialog.confirmInput = '';
    if (!device || !targetProcessId) {
      migrationDialog.loading = false;
      return;
    }

    migrationDialog.loading = true;
    try {
      const impact = await getDeviceProcessMigrationImpactApi(
        device.id,
        targetProcessId,
      );
      if (generation !== migrationRequestGeneration) return;
      migrationDialog.impact = impact;
    } catch (error) {
      if (generation !== migrationRequestGeneration) return;
      migrationDialog.error = errorMessage(error, '迁移影响预检失败，请重试。');
    } finally {
      if (generation === migrationRequestGeneration) {
        migrationDialog.loading = false;
      }
    }
  }

  async function submitMigration() {
    const device = migrationDialog.device;
    const impact = migrationDialog.impact;
    if (!canMigrateDevice.value
      || !device
      || !impact?.canMigrate
      || migrationDialog.confirmInput !== impact.confirmationText) {
      return;
    }

    submitting.value = true;
    try {
      await migrateDeviceProcessApi(device.id, {
        expectedSourceProcessId: impact.sourceProcess.id,
        targetProcessId: impact.targetProcess.id,
        expectedRowVersion: impact.rowVersion,
        confirmationText: migrationDialog.confirmInput,
      });
      closeMigrationDialog();
      if (selectedDevice.value?.id === device.id) {
        showDetailPanel.value = false;
        selectedDevice.value = null;
      }
      notifySuccess('设备工序已迁移，设备身份与客户端状态保持不变。');
      await refreshAfterMutation();
    } catch {
      /* feedback handled by http client */
    } finally {
      submitting.value = false;
    }
  }

  function errorMessage(error: unknown, fallback: string) {
    return error instanceof Error && error.message.trim()
      ? error.message
      : fallback;
  }

  return {
    authStore,
    devices: listPage.items,
    loading: listPage.loading,
    keyword,
    currentPage: listPage.page,
    metaData,
    submitting,
    selectedProcessId,
    processLoading,
    processError,
    listError,
    canUpdateDevice,
    canDeleteDevice,
    canMigrateDevice,
    processOptions,
    processNameMap,
    showRegisterModal,
    registerForm,
    showDetailPanel,
    selectedDevice,
    showEditModal,
    editForm,
    confirmDialog,
    deletionImpactRows,
    confirmDisabled,
    migrationDialog,
    initialize,
    fetchProcesses,
    fetchList,
    selectProcess,
    onSearchInput,
    onClearKeyword,
    onPageChange,
    processLabel,
    openRegisterModal,
    submitRegister,
    openDetailPanel,
    openEditModal,
    submitEdit,
    handleDelete,
    openMigrationDialog,
    selectMigrationTarget,
    submitMigration,
  };
}
