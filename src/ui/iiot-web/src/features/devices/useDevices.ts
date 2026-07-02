import { computed, reactive, ref } from 'vue';
import { useListPage } from '../../core/list-page';
import type { PagedMetaData } from '../../core/types/pagination';
import { getAllProcessesApi, type ProcessSelectDto } from '../processes/api';
import { useAuthStore } from '../../stores/auth';
import { Permissions } from '../../types/permissions';
import { notifySuccess, notifyWarning } from '../../utils/feedback';
import {
  deleteDeviceApi,
  getDeviceDeletionImpactApi,
  getDevicePagedListApi,
  registerDeviceApi,
  updateDeviceProfileApi,
  type DeviceDeletionImpactDto,
  type DeviceListItemDto,
} from './api';
import {
  isDeviceDeleteConfirmDisabled,
  type DeviceConfirmDialogState,
  type DeviceDeletionImpactRow,
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
  const allProcesses = ref<ProcessSelectDto[]>([]);
  const showRegisterModal = ref(false);
  const showDetailPanel = ref(false);
  const showEditModal = ref(false);
  const selectedDevice = ref<DeviceListItemDto | null>(null);
  const editTarget = ref<DeviceListItemDto | null>(null);
  const registerForm = reactive({ deviceName: '', processId: null as string | null });
  const editForm = reactive({ deviceName: '' });
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

  const listPage = useListPage<DeviceListItemDto, { keyword: string }>({
    initialFilter: { keyword: '' },
    initialPageSize: PAGE_SIZE,
    immediate: false,
    fetcher: async ({ page, pageSize, filter }) => {
      const response = await getDevicePagedListApi({
        PaginationParams: { PageNumber: page, PageSize: pageSize },
        Keyword: filter.keyword || undefined,
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
    authStore.hasPermission(Permissions.Device.Delete)
    && authStore.hasPermission(Permissions.Device.CascadeDelete),
  );
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
    ];
  });
  const confirmDisabled = computed(() =>
    isDeviceDeleteConfirmDisabled(confirmDialog.requiredText, confirmDialog.confirmInput),
  );

  let searchTimer: ReturnType<typeof setTimeout> | null = null;

  async function fetchProcesses() {
    try {
      allProcesses.value = await getAllProcessesApi();
    } catch {
      allProcesses.value = [];
    }
  }

  async function fetchList() {
    await listPage.refresh();
    if (listPage.error.value) {
      metaData.value = emptyMetaData();
      listPage.page.value = 1;
    }
  }

  async function initialize() {
    await Promise.all([fetchList(), fetchProcesses()]);
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
    registerForm.deviceName = '';
    registerForm.processId = null;
    await fetchProcesses();
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
        if (confirmDisabled.value) return;
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

  return {
    authStore,
    devices: listPage.items,
    loading: listPage.loading,
    keyword,
    currentPage: listPage.page,
    metaData,
    submitting,
    canUpdateDevice,
    canDeleteDevice,
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
    initialize,
    fetchList,
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
  };
}
