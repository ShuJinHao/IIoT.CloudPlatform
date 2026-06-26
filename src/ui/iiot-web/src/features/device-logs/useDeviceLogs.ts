import { computed, reactive, ref } from 'vue';
import type { PagedList } from '../../core/types/pagination';
import { getScopedDeviceSelectApi, type DeviceSelectDto } from '../devices/api';
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
  const currentMode = ref<DeviceLogQueryMode>('level');
  const selectedDeviceId = ref<string | null>(null);
  const loading = ref(false);
  const searched = ref(false);
  const currentPage = ref(1);
  const records = ref<DeviceLogListItemDto[]>([]);
  const metaData = ref(emptyDeviceLogMetaData());
  const filters = reactive(createDeviceLogFilters());
  const allDevices = ref<DeviceSelectDto[]>([]);
  const deviceLoadError = ref('');

  const deviceOptions = computed(() =>
    allDevices.value.map((device) => ({
      label: device.deviceName,
      value: device.id,
    })),
  );
  const rowKey = (row: DeviceLogListItemDto) => row.id;

  function resetResults() {
    currentPage.value = 1;
    records.value = [];
    metaData.value = emptyDeviceLogMetaData();
  }

  function switchMode(mode: DeviceLogQueryMode) {
    currentMode.value = mode;
    resetResults();
    searched.value = false;
    resetDeviceLogDateTime(filters);
  }

  function onDeviceChange() {
    resetResults();
    searched.value = false;
  }

  async function fetchDevices() {
    try {
      deviceLoadError.value = '';
      allDevices.value = await getScopedDeviceSelectApi();
    } catch {
      allDevices.value = [];
      deviceLoadError.value = '设备列表加载失败，请检查权限或稍后重试。';
    }
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
    if (!selectedDeviceId.value) {
      notifyWarning('请先选择设备。');
      return;
    }

    const validationMessage = validateDeviceLogSearch(currentMode.value, filters);
    if (validationMessage) {
      notifyWarning(validationMessage);
      return;
    }

    loading.value = true;
    searched.value = true;

    try {
      const response: PagedList<DeviceLogListItemDto> = await requestLogs(selectedDeviceId.value);
      metaData.value = response.metaData;
      records.value = response.items;
    } catch {
      records.value = [];
      metaData.value = emptyDeviceLogMetaData();
      currentPage.value = 1;
    } finally {
      loading.value = false;
    }
  }

  async function doSearch() {
    currentPage.value = 1;
    await fetchData();
  }

  async function onPageChange(page: number) {
    currentPage.value = page;
    await fetchData();
  }

  return {
    currentMode,
    selectedDeviceId,
    loading,
    searched,
    currentPage,
    records,
    metaData,
    filters,
    deviceLoadError,
    deviceOptions,
    fetchDevices,
    switchMode,
    onDeviceChange,
    doSearch,
    onPageChange,
    rowKey,
  };
}
