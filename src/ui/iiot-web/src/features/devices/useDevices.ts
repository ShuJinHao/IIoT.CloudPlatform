import axios from 'axios';
import { computed, reactive, ref } from 'vue';
import { useListPage } from '../../core/list-page';
import type { PagedMetaData } from '../../core/types/pagination';
import { ResultStatus } from '../../core/types/api';
import { useAuthStore } from '../../stores/auth';
import { Permissions } from '../../types/permissions';
import { notifySuccess, notifyWarning } from '../../utils/feedback';
import {
  deleteDeviceApi,
  getDeviceLedgerProcessOptionsApi,
  getAvailableDevicePluginSeriesApi,
  getDevicePagedListApi,
  getDeviceProcessMigrationImpactApi,
  migrateDeviceProcessApi,
  registerDeviceApi,
  updateDeviceProfileApi,
  type DeviceLedgerProcessOptionDto,
  type AvailableDevicePluginSeriesDto,
  type DeviceListItemDto,
} from './api';
import {
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
  const registerForm = reactive({
    deviceName: '',
    processId: null as string | null,
    pluginComponentId: null as string | null,
  });
  const availablePluginSeries = ref<AvailableDevicePluginSeriesDto[]>([]);
  const availablePluginSeriesLoading = ref(false);
  const availablePluginSeriesError = ref('');
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
  const availablePluginSeriesOptions = computed(() =>
    availablePluginSeries.value.map((series) => ({
      label: `${series.moduleId} · ${series.latestPublishedVersion}`,
      value: series.componentId,
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
    registerForm.pluginComponentId = null;
    availablePluginSeries.value = [];
    availablePluginSeriesError.value = '';
    showRegisterModal.value = true;
    availablePluginSeriesLoading.value = true;
    try {
      availablePluginSeries.value = await getAvailableDevicePluginSeriesApi(
        selectedProcessId.value,
      );
    } catch (error) {
      availablePluginSeriesError.value = errorMessage(
        error,
        '可用设备插件加载失败，请重试。',
      );
    } finally {
      availablePluginSeriesLoading.value = false;
    }
  }

  async function submitRegister() {
    const deviceName = registerForm.deviceName.trim();
    if (!deviceName || !registerForm.processId || !registerForm.pluginComponentId) {
      notifyWarning('请填写设备名称并选择已发布、未绑定的独立设备插件。');
      return;
    }
    submitting.value = true;
    try {
      const created = await registerDeviceApi({
        deviceName,
        processId: registerForm.processId,
        pluginComponentId: registerForm.pluginComponentId,
      });
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
    if (!canDeleteDevice.value || submitting.value) return;

    submitting.value = true;
    try {
      await deleteDeviceApi(device.id);
      if (selectedDevice.value?.id === device.id) {
        selectedDevice.value = null;
        showDetailPanel.value = false;
      }
      notifySuccess('设备及其可变关联数据已删除。');
      await refreshAfterMutation();
    } catch (error) {
      // 错误文案由 httpClient 原样呈现；404 只额外校正已失效的列表。
      if (isNotFoundError(error)) {
        await refreshAfterMutation();
      }
    } finally {
      submitting.value = false;
    }
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

  function isNotFoundError(error: unknown) {
    if (axios.isAxiosError(error)) {
      return error.response?.status === 404;
    }
    return typeof error === 'object'
      && error !== null
      && 'status' in error
      && error.status === ResultStatus.NotFound;
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
    availablePluginSeriesOptions,
    availablePluginSeriesLoading,
    availablePluginSeriesError,
    processNameMap,
    showRegisterModal,
    registerForm,
    showDetailPanel,
    selectedDevice,
    showEditModal,
    editForm,
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
