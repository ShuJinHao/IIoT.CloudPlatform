import { computed, reactive, ref } from 'vue';
import { useListPage } from '../../core/list-page';
import type { UiSelectOption } from '../../components/ui/types';
import { getAllActiveDevicesApi, type DeviceSelectDto } from '../devices/api';
import { getAllProcessesApi, type ProcessSelectDto } from '../processes/api';
import { useAuthStore } from '../../stores/auth';
import { Permissions } from '../../types/permissions';
import { notifySuccess, notifyWarning } from '../../utils/feedback';
import {
  addEdgeHostPlcBindingApi,
  createEdgeHostApi,
  deleteEdgeHostApi,
  disableEdgeHostApi,
  disableEdgeHostPlcBindingApi,
  enableEdgeHostApi,
  enableEdgeHostPlcBindingApi,
  getEdgeHostDetailApi,
  getEdgeHostPagedListApi,
  getEdgeHostPlcCapacitySummaryApi,
  getEdgeHostPlcRuntimeStatesApi,
  removeEdgeHostPlcBindingApi,
  updateEdgeHostApi,
  updateEdgeHostPlcBindingApi,
  type EdgeHostDto,
  type EdgeHostListItemDto,
  type EdgeHostPlcBindingDto,
  type EdgeHostPlcCapacitySummaryDto,
  type EdgeHostPlcRuntimeStateDto,
} from './api';
import {
  createEdgeHostColumns,
  createPlcBindingColumns,
  createPlcCapacitySummaryColumns,
  createPlcRuntimeStateColumns,
} from './columns';
import {
  copyPlcBindingToForm,
  createEmptyPlcBindingForm,
  EDGE_HOST_PAGE_SIZE,
  emptyEdgeHostMetaData,
  normalizeOptionalText,
  todayLocal,
  validateEdgeHostForm,
  validatePlcBindingForm,
  type EdgeHostConfirmDialog,
  type EdgeHostFormMode,
  type PlcBindingFormMode,
} from './types';

export function useEdgeHosts() {
  const authStore = useAuthStore();
  const submitting = ref(false);
  const detailLoading = ref(false);
  const runtimeLoading = ref(false);
  const capacityLoading = ref(false);
  const capacityDate = ref(todayLocal());
  const metaData = ref(emptyEdgeHostMetaData());
  const devices = ref<DeviceSelectDto[]>([]);
  const processes = ref<ProcessSelectDto[]>([]);
  const selectedHost = ref<EdgeHostDto | null>(null);
  const runtimeStates = ref<EdgeHostPlcRuntimeStateDto[]>([]);
  const capacitySummaries = ref<EdgeHostPlcCapacitySummaryDto[]>([]);
  const hostEditTarget = ref<EdgeHostListItemDto | null>(null);
  const plcEditTarget = ref<EdgeHostPlcBindingDto | null>(null);
  const hostFormMode = ref<EdgeHostFormMode>('create');
  const plcFormMode = ref<PlcBindingFormMode>('create');
  const showHostModal = ref(false);
  const showPlcDrawer = ref(false);
  const showPlcFormModal = ref(false);
  const hostForm = reactive({ deviceId: null as string | null, hostName: '', remark: '' });
  const plcForm = reactive(createEmptyPlcBindingForm());
  const confirmDialog = reactive<EdgeHostConfirmDialog>({
    show: false,
    title: '',
    desc: '',
    confirmText: '',
    tone: 'warning',
    onConfirm: async () => {},
  });

  const listPage = useListPage<EdgeHostListItemDto, { keyword: string }>({
    initialFilter: { keyword: '' },
    initialPageSize: EDGE_HOST_PAGE_SIZE,
    immediate: false,
    fetcher: async ({ page, pageSize, filter }) => {
      const response = await getEdgeHostPagedListApi({
        pagination: { PageNumber: page, PageSize: pageSize },
        keyword: filter.keyword || undefined,
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
  const canManage = computed(() => authStore.hasPermission(Permissions.EdgeHost.Manage));
  const deviceOptions = computed<UiSelectOption[]>(() =>
    devices.value.map((device) => ({
      label: `${device.deviceName} · ${device.code}`,
      value: device.id,
    })),
  );
  const processOptions = computed<UiSelectOption[]>(() =>
    processes.value.map((process) => ({
      label: `${process.processCode} · ${process.processName}`,
      value: process.id,
    })),
  );
  const selectedDeviceCode = computed(() =>
    devices.value.find((device) => device.id === hostForm.deviceId)?.code ?? '',
  );
  const selectedHostDeviceLabel = computed(() =>
    selectedHost.value ? deviceLabel(selectedHost.value.deviceId) : '-',
  );

  const hostColumns = computed(() => createEdgeHostColumns({
    canManage: () => canManage.value,
    deviceLabel,
    onConfigurePlc: openPlcDrawer,
    onEdit: openEditHostModal,
    onToggle: toggleHost,
    onDelete: askDeleteHost,
  }));
  const plcColumns = computed(() => createPlcBindingColumns({
    canManage: () => canManage.value,
    processLabel,
    deviceLabel,
    onEdit: openEditPlcModal,
    onToggle: togglePlcBinding,
    onRemove: askRemovePlcBinding,
  }));
  const runtimeColumns = computed(() => createPlcRuntimeStateColumns({
    processLabel,
    deviceLabel,
  }));
  const capacityColumns = computed(() => createPlcCapacitySummaryColumns({
    deviceLabel,
  }));

  let searchTimer: ReturnType<typeof setTimeout> | null = null;

  async function loadReferences() {
    const [deviceResult, processResult] = await Promise.allSettled([
      getAllActiveDevicesApi(),
      getAllProcessesApi(),
    ]);
    devices.value = deviceResult.status === 'fulfilled' ? deviceResult.value : [];
    processes.value = processResult.status === 'fulfilled' ? processResult.value : [];
  }

  async function fetchList() {
    await listPage.refresh();
    if (listPage.error.value) {
      metaData.value = emptyEdgeHostMetaData();
      listPage.page.value = 1;
    }
  }

  async function initialize() {
    await Promise.all([loadReferences(), fetchList()]);
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

  function deviceLabel(deviceId?: string | null) {
    if (!deviceId) return '未关联';
    const device = devices.value.find((item) => item.id === deviceId);
    return device ? `${device.deviceName} · ${device.code}` : `${deviceId.slice(0, 8)}...`;
  }

  function processLabel(processId?: string | null) {
    if (!processId) return '未关联';
    const process = processes.value.find((item) => item.id === processId);
    return process ? `${process.processCode} · ${process.processName}` : `${processId.slice(0, 8)}...`;
  }

  async function refreshAfterListMutation() {
    await fetchList();
    if (listPage.items.value.length === 0 && listPage.page.value > 1) {
      listPage.page.value -= 1;
      await fetchList();
    }
  }

  async function refreshSelectedHost() {
    if (!selectedHost.value) return;
    selectedHost.value = await getEdgeHostDetailApi(selectedHost.value.id);
  }

  async function refreshSelectedRuntimeStates(hostId = selectedHost.value?.id) {
    if (!hostId) {
      runtimeStates.value = [];
      return;
    }

    runtimeLoading.value = true;
    try {
      runtimeStates.value = await getEdgeHostPlcRuntimeStatesApi(hostId);
    } catch {
      runtimeStates.value = [];
    } finally {
      runtimeLoading.value = false;
    }
  }

  async function refreshSelectedCapacitySummaries(hostId = selectedHost.value?.id) {
    if (!hostId) {
      capacitySummaries.value = [];
      return;
    }

    capacityLoading.value = true;
    try {
      capacitySummaries.value = await getEdgeHostPlcCapacitySummaryApi(hostId, capacityDate.value);
    } catch {
      capacitySummaries.value = [];
    } finally {
      capacityLoading.value = false;
    }
  }

  async function openCreateHostModal() {
    await loadReferences();
    hostFormMode.value = 'create';
    hostEditTarget.value = null;
    hostForm.deviceId = null;
    hostForm.hostName = '';
    hostForm.remark = '';
    showHostModal.value = true;
  }

  async function openEditHostModal(host: EdgeHostListItemDto) {
    await loadReferences();
    hostFormMode.value = 'edit';
    hostEditTarget.value = host;
    hostForm.deviceId = host.deviceId;
    hostForm.hostName = host.hostName;
    hostForm.remark = host.remark ?? '';
    showHostModal.value = true;
  }

  async function submitHostForm() {
    const validation = validateEdgeHostForm(hostForm, hostFormMode.value);
    if (validation) {
      notifyWarning(validation);
      return;
    }

    submitting.value = true;
    try {
      if (hostFormMode.value === 'create') {
        const clientCode = selectedDeviceCode.value;
        if (!hostForm.deviceId || !clientCode) {
          notifyWarning('请选择带有 ClientCode 的云端设备。');
          return;
        }
        await createEdgeHostApi({
          deviceId: hostForm.deviceId,
          clientCode,
          hostName: hostForm.hostName.trim(),
          remark: normalizeOptionalText(hostForm.remark),
        });
        notifySuccess('上位机配置已创建。');
      } else if (hostEditTarget.value) {
        await updateEdgeHostApi(hostEditTarget.value.id, {
          edgeHostId: hostEditTarget.value.id,
          hostName: hostForm.hostName.trim(),
          remark: normalizeOptionalText(hostForm.remark),
        });
        notifySuccess('上位机配置已保存。');
      }

      showHostModal.value = false;
      await refreshAfterListMutation();
      if (selectedHost.value) await refreshSelectedHost();
    } catch {
      /* feedback handled by http client */
    } finally {
      submitting.value = false;
    }
  }

  async function openPlcDrawer(host: EdgeHostListItemDto) {
    showPlcDrawer.value = true;
    detailLoading.value = true;
    runtimeLoading.value = true;
    capacityLoading.value = true;
    capacityDate.value = todayLocal();
    selectedHost.value = null;
    runtimeStates.value = [];
    capacitySummaries.value = [];
    await loadReferences();
    try {
      const [detail, states, summaries] = await Promise.all([
        getEdgeHostDetailApi(host.id),
        getEdgeHostPlcRuntimeStatesApi(host.id),
        getEdgeHostPlcCapacitySummaryApi(host.id, capacityDate.value),
      ]);
      selectedHost.value = detail;
      runtimeStates.value = states;
      capacitySummaries.value = summaries;
    } catch {
      showPlcDrawer.value = false;
      runtimeStates.value = [];
      capacitySummaries.value = [];
    } finally {
      detailLoading.value = false;
      runtimeLoading.value = false;
      capacityLoading.value = false;
    }
  }

  function closePlcDrawer() {
    showPlcDrawer.value = false;
    selectedHost.value = null;
    runtimeStates.value = [];
    capacitySummaries.value = [];
  }

  function openCreatePlcModal() {
    if (!selectedHost.value || !canManage.value) return;
    plcFormMode.value = 'create';
    plcEditTarget.value = null;
    Object.assign(plcForm, createEmptyPlcBindingForm());
    showPlcFormModal.value = true;
  }

  function openEditPlcModal(binding: EdgeHostPlcBindingDto) {
    if (!selectedHost.value || !canManage.value) return;
    plcFormMode.value = 'edit';
    plcEditTarget.value = binding;
    Object.assign(plcForm, copyPlcBindingToForm(binding));
    showPlcFormModal.value = true;
  }

  async function submitPlcForm() {
    if (!selectedHost.value) return;
    const validation = validatePlcBindingForm(plcForm, plcFormMode.value);
    if (validation) {
      notifyWarning(validation);
      return;
    }

    submitting.value = true;
    try {
      if (plcFormMode.value === 'create') {
        selectedHost.value = await addEdgeHostPlcBindingApi(selectedHost.value.id, {
          edgeHostId: selectedHost.value.id,
          plcCode: plcForm.plcCode.trim(),
          plcName: plcForm.plcName.trim(),
          processId: plcForm.processId,
          businessDeviceId: plcForm.businessDeviceId,
          stationCode: normalizeOptionalText(plcForm.stationCode),
          protocol: normalizeOptionalText(plcForm.protocol),
          address: normalizeOptionalText(plcForm.address),
          displayOrder: plcForm.displayOrder ?? 0,
          remark: normalizeOptionalText(plcForm.remark),
          enabled: plcForm.enabled,
        });
        notifySuccess('PLC 绑定已新增。');
      } else if (plcEditTarget.value) {
        selectedHost.value = await updateEdgeHostPlcBindingApi(
          selectedHost.value.id,
          plcEditTarget.value.id,
          {
            edgeHostId: selectedHost.value.id,
            bindingId: plcEditTarget.value.id,
            plcName: plcForm.plcName.trim(),
            processId: plcForm.processId,
            businessDeviceId: plcForm.businessDeviceId,
            stationCode: normalizeOptionalText(plcForm.stationCode),
            protocol: normalizeOptionalText(plcForm.protocol),
            address: normalizeOptionalText(plcForm.address),
            displayOrder: plcForm.displayOrder ?? 0,
            remark: normalizeOptionalText(plcForm.remark),
          },
        );
        notifySuccess('PLC 绑定已保存。');
      }

      showPlcFormModal.value = false;
      await fetchList();
      await refreshSelectedRuntimeStates();
      await refreshSelectedCapacitySummaries();
    } catch {
      /* feedback handled by http client */
    } finally {
      submitting.value = false;
    }
  }

  async function toggleHost(host: EdgeHostListItemDto) {
    if (!canManage.value) return;
    submitting.value = true;
    try {
      const updated = host.enabled
        ? await disableEdgeHostApi(host.id)
        : await enableEdgeHostApi(host.id);
      notifySuccess(host.enabled ? '上位机配置已禁用。' : '上位机配置已启用。');
      if (selectedHost.value?.id === updated.id) selectedHost.value = updated;
      await fetchList();
    } catch {
      /* feedback handled by http client */
    } finally {
      submitting.value = false;
    }
  }

  async function togglePlcBinding(binding: EdgeHostPlcBindingDto) {
    if (!selectedHost.value || !canManage.value) return;
    submitting.value = true;
    try {
      selectedHost.value = binding.enabled
        ? await disableEdgeHostPlcBindingApi(selectedHost.value.id, binding.id)
        : await enableEdgeHostPlcBindingApi(selectedHost.value.id, binding.id);
      notifySuccess(binding.enabled ? 'PLC 绑定已禁用。' : 'PLC 绑定已启用。');
      await fetchList();
      await refreshSelectedRuntimeStates();
      await refreshSelectedCapacitySummaries();
    } catch {
      /* feedback handled by http client */
    } finally {
      submitting.value = false;
    }
  }

  function askDeleteHost(host: EdgeHostListItemDto) {
    if (!canManage.value) return;
    Object.assign(confirmDialog, {
      show: true,
      tone: 'error',
      title: '确认删除上位机配置',
      desc: `将删除【${host.hostName}】及其 ${host.plcBindingCount} 个 PLC 绑定配置。该操作不会删除设备主数据。`,
      confirmText: '确认删除',
      onConfirm: async () => {
        submitting.value = true;
        try {
          await deleteEdgeHostApi(host.id);
          if (selectedHost.value?.id === host.id) closePlcDrawer();
          confirmDialog.show = false;
          notifySuccess('上位机配置已删除。');
          await refreshAfterListMutation();
        } catch {
          /* feedback handled by http client */
        } finally {
          submitting.value = false;
        }
      },
    });
  }

  function askRemovePlcBinding(binding: EdgeHostPlcBindingDto) {
    if (!selectedHost.value || !canManage.value) return;
    Object.assign(confirmDialog, {
      show: true,
      tone: 'error',
      title: '确认删除 PLC 绑定',
      desc: `将删除【${binding.plcCode} · ${binding.plcName}】这条 Cloud 配置。该操作不会操作现场 PLC。`,
      confirmText: '确认删除',
      onConfirm: async () => {
        if (!selectedHost.value) return;
        submitting.value = true;
        try {
          selectedHost.value = await removeEdgeHostPlcBindingApi(selectedHost.value.id, binding.id);
          confirmDialog.show = false;
          notifySuccess('PLC 绑定已删除。');
          await fetchList();
          await refreshSelectedRuntimeStates();
          await refreshSelectedCapacitySummaries();
        } catch {
          /* feedback handled by http client */
        } finally {
          submitting.value = false;
        }
      },
    });
  }

  return {
    hosts: listPage.items,
    loading: listPage.loading,
    keyword,
    currentPage: listPage.page,
    metaData,
    submitting,
    detailLoading,
    runtimeLoading,
    capacityLoading,
    canManage,
    deviceOptions,
    processOptions,
    selectedDeviceCode,
    selectedHost,
    runtimeStates,
    capacitySummaries,
    capacityDate,
    selectedHostDeviceLabel,
    hostFormMode,
    plcFormMode,
    hostForm,
    plcForm,
    showHostModal,
    showPlcDrawer,
    showPlcFormModal,
    confirmDialog,
    hostColumns,
    plcColumns,
    runtimeColumns,
    capacityColumns,
    initialize,
    fetchList,
    onSearchInput,
    onClearKeyword,
    onPageChange,
    openCreateHostModal,
    submitHostForm,
    closePlcDrawer,
    openCreatePlcModal,
    submitPlcForm,
  };
}
