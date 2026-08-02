import { computed, reactive, ref, watch } from 'vue';
import { getScopedDeviceSelectApi, type ScopedDeviceSelectDto } from '../devices/api';
import type { PagedMetaData } from '../../core/types/pagination';
import { notifySuccess, notifyWarning } from '../../utils/feedback';
import {
  exportPassStationsApi,
  getPassStationDetailApi,
  getPassStationListApi,
  getPassStationTypesApi,
  type PassStationDetailDto,
  type PassStationListItemDto,
  type GetPassStationListParams,
  type PassStationQueryMode,
} from './api';
import { createPassStationColumns } from './columns';
import {
  buildPassStationSchemaMap,
  getPassStationSchema,
  normalizePassStationTypeKey,
  type PassStationSchema,
} from './schema';
import {
  defaultEndTime,
  defaultStartTime,
  PAGE_SIZE,
  queryModeLabels,
  toUtcIso,
  type PassStationFilters,
  type PassStationProcessOption,
} from './types';

const emptyMetaData = (): PagedMetaData => ({
  totalCount: 0,
  pageSize: PAGE_SIZE,
  currentPage: 1,
  totalPages: 1,
});

export function usePassStation() {
  const loading = ref(false);
  const selectLoading = ref(false);
  const selectError = ref<string | null>(null);
  const queryError = ref<string | null>(null);
  const exporting = ref(false);
  const searched = ref(false);
  const currentPage = ref(1);
  const currentMode = ref<PassStationQueryMode>('barcode-process');
  const currentProcessId = ref<string | null>(null);
  const records = ref<PassStationListItemDto[]>([]);
  const metaData = ref<PagedMetaData>(emptyMetaData());
  const allProcesses = ref<PassStationProcessOption[]>([]);
  const allDevices = ref<ScopedDeviceSelectDto[]>([]);
  const schemaMap = ref<Record<string, PassStationSchema>>({});
  const showDetail = ref(false);
  const detailLoading = ref(false);
  const detailData = ref<PassStationDetailDto | null>(null);
  const filters = reactive<PassStationFilters>({
    deviceId: null,
    barcode: '',
    startTime: defaultStartTime(),
    endTime: defaultEndTime(),
  });

  const currentProcess = computed(() =>
    allProcesses.value.find((process) => process.id === currentProcessId.value) ?? null);
  const currentTypeKey = computed(() =>
    currentProcess.value ? normalizePassStationTypeKey(currentProcess.value.processCode) : '');
  const currentSchema = computed(() =>
    getPassStationSchema(schemaMap.value, currentTypeKey.value));
  const supportedProcesses = computed(() =>
    allProcesses.value.filter((process) =>
      Boolean(schemaMap.value[normalizePassStationTypeKey(process.processCode)])));
  const processOptions = computed(() => supportedProcesses.value.map((process) => ({
    label: `${process.processCode} - ${process.processName}`,
    value: process.id,
  })));
  const filteredDevices = computed(() => {
    if (!currentProcessId.value) return [] as ScopedDeviceSelectDto[];
    return allDevices.value.filter((device) => device.processId === currentProcessId.value);
  });
  const deviceOptions = computed(() => filteredDevices.value.map((device) => ({
    label: device.deviceName,
    value: device.id,
  })));
  const activeQueryModes = computed(() => {
    if (!currentSchema.value) return [];
    return currentSchema.value.supportedModes.map((mode) => ({ key: mode, label: queryModeLabels[mode] }));
  });
  const columns = computed(() => createPassStationColumns(currentSchema.value));
  const rowKey = (row: PassStationListItemDto) => row.id;
  const rowProps = (row: PassStationListItemDto) => ({
    style: 'cursor: pointer;',
    onClick: () => openDetail(row.id),
  });

  function resetResults() {
    currentPage.value = 1;
    records.value = [];
    metaData.value = emptyMetaData();
  }

  async function fetchSelectData() {
    selectLoading.value = true;
    selectError.value = null;
    try {
      const [devices, schemas] = await Promise.all([
        getScopedDeviceSelectApi(),
        getPassStationTypesApi(),
      ]);
      allDevices.value = devices;
      const processById = new Map<string, PassStationProcessOption>();
      devices.forEach((device) => {
        processById.set(device.processId, {
          id: device.processId,
          processCode: device.processCode,
          processName: device.processName,
        });
      });
      allProcesses.value = [...processById.values()];
      schemaMap.value = buildPassStationSchemaMap(schemas);

      const firstSupported = supportedProcesses.value[0];
      if (!currentProcessId.value && firstSupported) currentProcessId.value = firstSupported.id;
    } catch {
      allProcesses.value = [];
      allDevices.value = [];
      schemaMap.value = {};
      currentProcessId.value = null;
      selectError.value = '授权设备与过站契约加载失败，请重试。';
    } finally {
      selectLoading.value = false;
    }
  }

  function buildCurrentQueryParams(): GetPassStationListParams | null {
    if (!currentSchema.value || !currentProcess.value) return null;
    return {
      typeKey: currentSchema.value.typeKey,
      mode: currentMode.value,
      pagination: { PageNumber: currentPage.value, PageSize: PAGE_SIZE },
      processId: currentMode.value === 'barcode-process' || currentMode.value === 'time-process'
        ? currentProcess.value.id
        : undefined,
      deviceId: currentMode.value === 'device-barcode' || currentMode.value === 'device-time' || currentMode.value === 'device-latest'
        ? filters.deviceId || undefined
        : undefined,
      barcode: currentMode.value === 'barcode-process' || currentMode.value === 'device-barcode'
        ? filters.barcode.trim()
        : undefined,
      startTime: currentMode.value === 'time-process' || currentMode.value === 'device-time'
        ? toUtcIso(filters.startTime)
        : undefined,
      endTime: currentMode.value === 'time-process' || currentMode.value === 'device-time'
        ? toUtcIso(filters.endTime)
        : undefined,
    };
  }

  async function fetchData() {
    if (!currentSchema.value || !currentProcess.value) {
      notifyWarning('请先选择已支持追溯的工序。');
      return;
    }

    loading.value = true;
    searched.value = true;
    queryError.value = null;
    try {
      const params = buildCurrentQueryParams();
      if (!params) return;
      const response = await getPassStationListApi(params);
      metaData.value = response.metaData;
      records.value = response.items;
    } catch {
      records.value = [];
      metaData.value = emptyMetaData();
      currentPage.value = 1;
      queryError.value = '过站记录加载失败，请重试。';
    } finally {
      loading.value = false;
    }
  }

  async function doSearch() {
    const validationMessage = validateCurrentQuery();
    if (validationMessage) {
      notifyWarning(validationMessage);
      return;
    }
    currentPage.value = 1;
    await fetchData();
  }

  async function doExport() {
    const validationMessage = validateCurrentQuery();
    if (validationMessage) {
      notifyWarning(validationMessage);
      return;
    }
    const params = buildCurrentQueryParams();
    if (!params) return;

    exporting.value = true;
    try {
      const download = await exportPassStationsApi(params);
      const url = URL.createObjectURL(download.blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = download.fileName;
      anchor.click();
      URL.revokeObjectURL(url);
      notifySuccess('过站 CSV 已生成。');
    } finally {
      exporting.value = false;
    }
  }

  async function onPageChange(page: number) {
    currentPage.value = page;
    await fetchData();
  }

  async function openDetail(id: string) {
    if (!currentSchema.value) return;
    showDetail.value = true;
    detailLoading.value = true;
    detailData.value = null;
    try {
      detailData.value = await getPassStationDetailApi(currentSchema.value.typeKey, id);
    } catch {
      showDetail.value = false;
    } finally {
      detailLoading.value = false;
    }
  }

  function switchMode(mode: PassStationQueryMode) {
    currentMode.value = mode;
    resetResults();
    searched.value = false;
    if (mode === 'time-process' || mode === 'device-time') {
      filters.startTime = defaultStartTime();
      filters.endTime = defaultEndTime();
    }
  }

  function validateCurrentQuery(): string | null {
    if (!currentSchema.value || !currentProcess.value) return '请先选择已支持追溯的工序。';
    if ((currentMode.value === 'barcode-process' || currentMode.value === 'device-barcode') && !filters.barcode.trim()) {
      return '当前查询模式必须填写条码。';
    }
    if (
      (currentMode.value === 'device-barcode' || currentMode.value === 'device-time' || currentMode.value === 'device-latest') &&
      !filters.deviceId
    ) {
      return '请选择设备。';
    }
    if ((currentMode.value === 'time-process' || currentMode.value === 'device-time') && (!filters.startTime || !filters.endTime)) {
      return '请同时填写开始时间和结束时间。';
    }
    return null;
  }

  watch(currentSchema, (schema) => {
    if (!schema) {
      resetResults();
      searched.value = false;
      filters.deviceId = null;
      return;
    }
    if (!schema.supportedModes.includes(currentMode.value)) {
      const next = schema.supportedModes[0];
      if (next) currentMode.value = next;
    }
  });

  watch(currentProcessId, () => {
    resetResults();
    searched.value = false;
    filters.deviceId = null;
  });

  return {
    PAGE_SIZE,
    loading,
    selectLoading,
    selectError,
    queryError,
    exporting,
    searched,
    currentPage,
    currentMode,
    currentProcessId,
    records,
    metaData,
    filters,
    currentProcess,
    currentSchema,
    processOptions,
    deviceOptions,
    activeQueryModes,
    columns,
    rowKey,
    rowProps,
    showDetail,
    detailLoading,
    detailData,
    fetchSelectData,
    doSearch,
    doExport,
    onPageChange,
    switchMode,
  };
}
