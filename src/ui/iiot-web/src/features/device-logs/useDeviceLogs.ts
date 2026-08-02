import { reactive, ref, watch } from 'vue';
import { resolveRequestErrorMessage } from '../../core/http/resolveRequestError';
import type { PagedList } from '../../core/types/pagination';
import { useProductionContext } from '../../shared/production-context';
import { notifyWarning } from '../../utils/feedback';
import {
  getLogsByDeviceAndDateApi,
  getLogsByDeviceAndKeywordApi,
  getLogsByDeviceAndLevelApi,
  getLogsByDeviceAndTimeRangeApi,
  getLogsByDeviceDateAndKeywordApi,
  type DeviceLogListItemDto,
} from './api';
import {
  createDeviceLogFilters,
  DEVICE_LOG_PAGE_SIZE,
  emptyDeviceLogMetaData,
  resetDeviceLogDateTime,
  toUtcIso,
  validateDeviceLogSearch,
  type DeviceLogQueryMode,
} from './types';

export function useDeviceLogs() {
  const productionContext = useProductionContext();
  const {
    processOptions,
    deviceOptions,
    selectedProcessId,
    selectedDeviceId,
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
  const currentMode = ref<DeviceLogQueryMode>('level');
  const loading = ref(false);
  const queryError = ref('');
  const searched = ref(false);
  const currentPage = ref(1);
  const records = ref<DeviceLogListItemDto[]>([]);
  const metaData = ref(emptyDeviceLogMetaData());
  const filters = reactive(createDeviceLogFilters());
  let requestGeneration = 0;

  const rowKey = (row: DeviceLogListItemDto) => row.id;

  function resetResults() {
    requestGeneration++;
    loading.value = false;
    queryError.value = '';
    searched.value = false;
    currentPage.value = 1;
    records.value = [];
    metaData.value = emptyDeviceLogMetaData();
  }

  function switchMode(mode: DeviceLogQueryMode) {
    currentMode.value = mode;
    resetResults();
    resetDeviceLogDateTime(filters);
  }

  async function requestLogs(deviceId: string) {
    const pagination = {
      PageNumber: currentPage.value,
      PageSize: DEVICE_LOG_PAGE_SIZE,
    };

    switch (currentMode.value) {
      case 'level':
        return getLogsByDeviceAndLevelApi({
          pagination,
          deviceId,
          level: filters.level || undefined,
        });
      case 'keyword':
        return getLogsByDeviceAndKeywordApi({
          pagination,
          deviceId,
          keyword: filters.keyword.trim(),
        });
      case 'date':
        return getLogsByDeviceAndDateApi({
          pagination,
          deviceId,
          date: filters.date,
        });
      case 'time-range':
        return getLogsByDeviceAndTimeRangeApi({
          pagination,
          deviceId,
          startTime: toUtcIso(filters.startTime),
          endTime: toUtcIso(filters.endTime),
        });
      case 'date-keyword':
        return getLogsByDeviceDateAndKeywordApi({
          pagination,
          deviceId,
          date: filters.date,
          keyword: filters.keyword.trim(),
        });
    }
  }

  async function fetchData() {
    const activeContext = context.value;
    if (!activeContext) return;
    const generation = ++requestGeneration;
    loading.value = true;
    searched.value = true;
    queryError.value = '';
    records.value = [];
    metaData.value = emptyDeviceLogMetaData();
    try {
      const response: PagedList<DeviceLogListItemDto> = await requestLogs(
        activeContext.deviceId,
      );
      if (generation !== requestGeneration) return;
      metaData.value = response.metaData;
      records.value = response.items;
    } catch (error) {
      const message = await resolveRequestErrorMessage(
        error,
        '设备日志加载失败，请检查服务状态后重试。',
      );
      if (generation === requestGeneration) queryError.value = message;
    } finally {
      if (generation === requestGeneration) loading.value = false;
    }
  }

  async function doSearch() {
    if (!context.value) {
      notifyWarning('请先完整选择工序和设备。');
      return;
    }
    const validationMessage = validateDeviceLogSearch(currentMode.value, filters);
    if (validationMessage) {
      notifyWarning(validationMessage);
      return;
    }
    currentPage.value = 1;
    await fetchData();
  }

  async function onPageChange(page: number) {
    currentPage.value = page;
    await fetchData();
  }

  watch(selectionRevision, resetResults, { flush: 'sync' });

  return {
    currentMode,
    loading,
    queryError,
    searched,
    currentPage,
    records,
    metaData,
    filters,
    processOptions,
    deviceOptions,
    selectedProcessId,
    selectedDeviceId,
    context,
    contextStatus,
    contextError,
    contextState,
    hasAuthorizedDevices,
    initialize: loadContext,
    selectProcess,
    selectDevice,
    switchMode,
    doSearch,
    onPageChange,
    rowKey,
  };
}
