import { computed, reactive, ref, watch } from 'vue';
import type { PagedMetaData } from '../../core/types/pagination';
import { resolveRequestErrorMessage } from '../../core/http/resolveRequestError';
import { useProductionContext } from '../../shared/production-context';
import { notifySuccess, notifyWarning } from '../../utils/feedback';
import {
  exportPassStationsApi,
  getPassStationDetailApi,
  getPassStationListApi,
  getPassStationTypesApi,
  type GetPassStationListParams,
  type PassStationDetailDto,
  type PassStationListItemDto,
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
  deviceQueryModes,
  PAGE_SIZE,
  queryModeLabels,
  toUtcIso,
  type DevicePassStationQueryMode,
  type PassStationFilters,
} from './types';

const emptyMetaData = (): PagedMetaData => ({
  totalCount: 0,
  pageSize: PAGE_SIZE,
  currentPage: 1,
  totalPages: 1,
});

export function usePassStation() {
  const productionContext = useProductionContext();
  const {
    processOptions,
    deviceOptions,
    selectedProcessId,
    selectedDeviceId,
    selectedProcess,
    context,
    status: contextStatus,
    error: contextError,
    state: contextState,
    selectionRevision,
    hasAuthorizedDevices,
    loadContext,
    selectProcess,
    selectDevice,
  } = productionContext;
  const loading = ref(false);
  const schemaLoading = ref(false);
  const schemaError = ref('');
  const queryError = ref<string | null>(null);
  const exporting = ref(false);
  const searched = ref(false);
  const currentPage = ref(1);
  const currentMode = ref<DevicePassStationQueryMode>('device-barcode');
  const records = ref<PassStationListItemDto[]>([]);
  const metaData = ref<PagedMetaData>(emptyMetaData());
  const schemaMap = ref<Record<string, PassStationSchema>>({});
  const showDetail = ref(false);
  const detailLoading = ref(false);
  const detailError = ref('');
  const detailData = ref<PassStationDetailDto | null>(null);
  const detailId = ref<string | null>(null);
  const filters = reactive<PassStationFilters>({
    barcode: '',
    startTime: defaultStartTime(),
    endTime: defaultEndTime(),
  });
  let schemaGeneration = 0;
  let queryGeneration = 0;
  let detailGeneration = 0;
  let exportGeneration = 0;

  const currentTypeKey = computed(() =>
    selectedProcess.value
      ? normalizePassStationTypeKey(selectedProcess.value.processCode)
      : '');
  const currentSchema = computed(() =>
    getPassStationSchema(schemaMap.value, currentTypeKey.value));
  const activeQueryModes = computed(() => {
    if (!currentSchema.value) return [];
    return currentSchema.value.supportedModes
      .filter((mode): mode is DevicePassStationQueryMode =>
        deviceQueryModes.some((candidate) => candidate === mode))
      .map((mode) => ({ key: mode, label: queryModeLabels[mode] }));
  });
  const hasDeviceQueryMode = computed(() => activeQueryModes.value.length > 0);
  const columns = computed(() => createPassStationColumns(currentSchema.value));
  const rowKey = (row: PassStationListItemDto) => row.id;
  const rowProps = (row: PassStationListItemDto) => ({
    style: 'cursor: pointer;',
    onClick: () => openDetail(row.id),
  });

  function clearDetail() {
    detailGeneration++;
    showDetail.value = false;
    detailLoading.value = false;
    detailError.value = '';
    detailData.value = null;
    detailId.value = null;
  }

  function resetResults() {
    queryGeneration++;
    exportGeneration++;
    loading.value = false;
    exporting.value = false;
    queryError.value = null;
    searched.value = false;
    currentPage.value = 1;
    records.value = [];
    metaData.value = emptyMetaData();
    clearDetail();
  }

  async function loadSchemas() {
    const generation = ++schemaGeneration;
    schemaLoading.value = true;
    schemaError.value = '';
    schemaMap.value = {};
    try {
      const schemas = await getPassStationTypesApi();
      if (generation !== schemaGeneration) return;
      schemaMap.value = buildPassStationSchemaMap(schemas);
    } catch (error) {
      const message = await resolveRequestErrorMessage(
        error,
        '过站查询契约加载失败，请检查服务状态后重试。',
      );
      if (generation !== schemaGeneration) return;
      schemaError.value = message;
    } finally {
      if (generation === schemaGeneration) schemaLoading.value = false;
    }
  }

  async function initialize() {
    await Promise.all([loadContext(), loadSchemas()]);
  }

  function buildCurrentQueryParams(): GetPassStationListParams | null {
    const activeContext = context.value;
    const schema = currentSchema.value;
    if (!activeContext || !schema || !hasDeviceQueryMode.value) return null;
    return {
      typeKey: schema.typeKey,
      mode: currentMode.value,
      pagination: { PageNumber: currentPage.value, PageSize: PAGE_SIZE },
      deviceId: activeContext.deviceId,
      barcode: currentMode.value === 'device-barcode'
        ? filters.barcode.trim()
        : undefined,
      startTime: currentMode.value === 'device-time'
        ? toUtcIso(filters.startTime)
        : undefined,
      endTime: currentMode.value === 'device-time'
        ? toUtcIso(filters.endTime)
        : undefined,
    };
  }

  async function fetchData() {
    const params = buildCurrentQueryParams();
    if (!params) return;
    const generation = ++queryGeneration;
    loading.value = true;
    searched.value = true;
    queryError.value = null;
    records.value = [];
    metaData.value = emptyMetaData();
    try {
      const response = await getPassStationListApi(params);
      if (generation !== queryGeneration) return;
      metaData.value = response.metaData;
      records.value = response.items;
    } catch (error) {
      const message = await resolveRequestErrorMessage(
        error,
        '过站记录加载失败，请检查服务状态后重试。',
      );
      if (generation !== queryGeneration) return;
      queryError.value = message;
    } finally {
      if (generation === queryGeneration) loading.value = false;
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

    const generation = ++exportGeneration;
    exporting.value = true;
    try {
      const download = await exportPassStationsApi(params);
      if (generation !== exportGeneration) return;
      const url = URL.createObjectURL(download.blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = download.fileName;
      anchor.click();
      URL.revokeObjectURL(url);
      notifySuccess('过站 CSV 已生成。');
    } catch (error) {
      const message = await resolveRequestErrorMessage(
        error,
        '过站 CSV 导出失败，请检查服务状态后重试。',
      );
      if (generation === exportGeneration) notifyWarning(message);
    } finally {
      if (generation === exportGeneration) exporting.value = false;
    }
  }

  async function onPageChange(page: number) {
    currentPage.value = page;
    await fetchData();
  }

  async function openDetail(id: string) {
    const activeContext = context.value;
    const schema = currentSchema.value;
    if (!activeContext || !schema) return;
    const generation = ++detailGeneration;
    detailId.value = id;
    showDetail.value = true;
    detailLoading.value = true;
    detailError.value = '';
    detailData.value = null;
    try {
      const detail = await getPassStationDetailApi(schema.typeKey, id);
      if (generation !== detailGeneration) return;
      if (detail.deviceId !== activeContext.deviceId) {
        detailError.value = '该记录已不属于当前设备上下文，请重新查询。';
        return;
      }
      detailData.value = detail;
    } catch (error) {
      const message = await resolveRequestErrorMessage(
        error,
        '过站详情加载失败，请检查服务状态后重试。',
      );
      if (generation === detailGeneration) detailError.value = message;
    } finally {
      if (generation === detailGeneration) detailLoading.value = false;
    }
  }

  function retryDetail() {
    if (detailId.value) void openDetail(detailId.value);
  }

  function switchMode(mode: DevicePassStationQueryMode) {
    if (!activeQueryModes.value.some((item) => item.key === mode)) return;
    currentMode.value = mode;
    resetResults();
    if (mode === 'device-time') {
      filters.startTime = defaultStartTime();
      filters.endTime = defaultEndTime();
    }
  }

  function validateCurrentQuery(): string | null {
    if (!context.value) return '请先完整选择工序和设备。';
    if (!currentSchema.value) return '当前工序尚未接入过站追溯能力。';
    if (!hasDeviceQueryMode.value) return '当前工序不支持设备级过站查询。';
    if (currentMode.value === 'device-barcode' && !filters.barcode.trim()) {
      return '当前查询模式必须填写弹夹号。';
    }
    if (currentMode.value === 'device-time' && (!filters.startTime || !filters.endTime)) {
      return '请同时填写开始时间和结束时间。';
    }
    return null;
  }

  watch(selectionRevision, resetResults, { flush: 'sync' });
  watch(showDetail, (visible) => {
    if (visible) return;
    detailGeneration++;
    detailLoading.value = false;
    detailError.value = '';
    detailData.value = null;
    detailId.value = null;
  }, { flush: 'sync' });
  watch(activeQueryModes, (modes) => {
    if (modes.some((mode) => mode.key === currentMode.value)) return;
    currentMode.value = modes[0]?.key ?? 'device-barcode';
    resetResults();
  }, { flush: 'sync' });

  return {
    PAGE_SIZE,
    loading,
    schemaLoading,
    schemaError,
    queryError,
    exporting,
    searched,
    currentPage,
    currentMode,
    records,
    metaData,
    filters,
    currentSchema,
    processOptions,
    deviceOptions,
    activeQueryModes,
    hasDeviceQueryMode,
    columns,
    rowKey,
    rowProps,
    showDetail,
    detailLoading,
    detailError,
    detailData,
    context,
    contextStatus,
    contextError,
    contextState,
    selectedProcessId,
    selectedDeviceId,
    hasAuthorizedDevices,
    initialize,
    loadSchemas,
    selectProcess,
    selectDevice,
    doSearch,
    doExport,
    onPageChange,
    switchMode,
    retryDetail,
  };
}
