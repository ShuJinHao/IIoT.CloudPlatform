import { computed, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { useListPage } from '../../core/list-page';
import { getScopedDeviceSelectApi, type DeviceSelectDto } from '../devices/api';
import { getDailyPagedApi, type DailyCapacityItem } from './api';
import { resolveCapacityLoadError, type CapacityLoadError } from './errors';
import { CAPACITY_PAGE_SIZE, todayLocal } from './types';

interface CapacityDashboardFilter extends Record<string, unknown> {
  deviceId: string | null;
  date: string;
}

export function useCapacityDashboard() {
  const router = useRouter();
  const allDevices = ref<DeviceSelectDto[]>([]);
  const deviceLoadError = ref('');
  const listError = ref<CapacityLoadError | null>(null);
  let listErrorGeneration = 0;

  const listPage = useListPage<DailyCapacityItem, CapacityDashboardFilter>({
    initialFilter: { deviceId: null, date: todayLocal() },
    initialPageSize: CAPACITY_PAGE_SIZE,
    immediate: false,
    fetcher: async ({ page, pageSize, filter }) => {
      const response = await getDailyPagedApi({
        PageNumber: page,
        PageSize: pageSize,
        date: filter.date || undefined,
        deviceId: filter.deviceId || undefined,
      });
      return {
        items: response.items,
        total: response.metaData.totalCount,
      };
    },
  });

  const deviceFilter = computed({
    get: () => listPage.filter.deviceId,
    set: (value: string | null) => {
      listPage.filter.deviceId = value;
    },
  });
  const dateFilter = computed({
    get: () => listPage.filter.date,
    set: (value: string) => {
      listPage.filter.date = value;
    },
  });
  const metaData = computed(() => ({
    totalCount: listPage.total.value,
    pageSize: CAPACITY_PAGE_SIZE,
    currentPage: listPage.page.value,
    totalPages: listPage.totalPages.value,
  }));
  const deviceOptions = computed(() =>
    allDevices.value.map((device) => ({
      label: device.deviceName,
      value: device.id,
    })),
  );
  const totalStats = computed(() => {
    const total = listPage.items.value.reduce((sum, row) => sum + row.totalCount, 0);
    const ok = listPage.items.value.reduce((sum, row) => sum + row.okCount, 0);
    const ng = listPage.items.value.reduce((sum, row) => sum + row.ngCount, 0);
    const ratePercent = total > 0 ? (ok * 100) / total : 0;
    return { total, ok, ng, ratePercent };
  });

  watch(listPage.error, async (error) => {
    const generation = ++listErrorGeneration;
    if (!error) {
      listError.value = null;
      return;
    }
    const resolved = await resolveCapacityLoadError(error);
    if (generation === listErrorGeneration) listError.value = resolved;
  });

  async function fetchDevices() {
    try {
      deviceLoadError.value = '';
      allDevices.value = await getScopedDeviceSelectApi();
    } catch (error) {
      allDevices.value = [];
      deviceLoadError.value = (await resolveCapacityLoadError(error)).message;
    }
  }

  async function fetchData() {
    await listPage.refresh();
    if (listPage.error.value) {
      listPage.page.value = 1;
    }
  }

  async function initialize() {
    await Promise.all([fetchDevices(), fetchData()]);
  }

  function onFilterChange() {
    listPage.page.value = 1;
    void fetchData();
  }

  function clearFilters() {
    listPage.filter.deviceId = null;
    listPage.filter.date = todayLocal();
    listPage.page.value = 1;
    void fetchData();
  }

  function onPageChange(page: number) {
    listPage.gotoPage(page);
  }

  function goDetail(deviceId: string, deviceName: string) {
    if (!deviceId) return;
    void router.push({
      name: 'CapacityDetail',
      query: { deviceId, deviceName },
    });
  }

  const rowKey = (row: DailyCapacityItem) => `${row.deviceId}-${row.date}`;

  return {
    records: listPage.items,
    loading: listPage.loading,
    currentPage: listPage.page,
    metaData,
    deviceFilter,
    dateFilter,
    deviceOptions,
    deviceLoadError,
    listError,
    totalStats,
    initialize,
    fetchData,
    onFilterChange,
    clearFilters,
    onPageChange,
    goDetail,
    rowKey,
  };
}
