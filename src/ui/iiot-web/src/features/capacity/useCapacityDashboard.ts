import { computed, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { useListPage } from '../../core/list-page';
import { useProductionContext } from '../../shared/production-context';
import { getDailyPagedApi, type DailyCapacityItem } from './api';
import { resolveCapacityLoadError, type CapacityLoadError } from './errors';
import { CAPACITY_PAGE_SIZE, todayLocal } from './types';

interface CapacityDashboardFilter extends Record<string, unknown> {
  date: string;
}

export function useCapacityDashboard() {
  const router = useRouter();
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
  const listError = ref<CapacityLoadError | null>(null);
  let listErrorGeneration = 0;

  const listPage = useListPage<DailyCapacityItem, CapacityDashboardFilter>({
    initialFilter: { date: todayLocal() },
    initialPageSize: CAPACITY_PAGE_SIZE,
    immediate: false,
    fetcher: async ({ page, pageSize, filter }) => {
      const activeContext = context.value;
      if (!activeContext) return { items: [], total: 0 };
      const response = await getDailyPagedApi({
        PageNumber: page,
        PageSize: pageSize,
        date: filter.date || undefined,
        deviceId: activeContext.deviceId,
      });
      return {
        items: response.items,
        total: response.metaData.totalCount,
      };
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
  const totalStats = computed(() => {
    const total = listPage.items.value.reduce((sum, row) => sum + row.totalCount, 0);
    const qualityKnown = listPage.items.value.every(
      (row) => row.okCount !== null && row.ngCount !== null,
    );
    const ok = qualityKnown
      ? listPage.items.value.reduce((sum, row) => sum + row.okCount!, 0)
      : null;
    const ng = qualityKnown
      ? listPage.items.value.reduce((sum, row) => sum + row.ngCount!, 0)
      : null;
    const ratePercent = ok === null ? null : total > 0 ? (ok * 100) / total : 0;
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

  async function fetchData() {
    if (!context.value) {
      listPage.clear();
      return;
    }
    listPage.clear();
    await listPage.refresh();
  }

  async function initialize() {
    await loadContext();
  }

  function resetPageAndFetch() {
    listPage.clear();
    listError.value = null;
    if (listPage.page.value !== 1) {
      listPage.page.value = 1;
      return;
    }
    if (context.value) void fetchData();
  }

  function clearFilters() {
    listPage.filter.date = todayLocal();
    resetPageAndFetch();
  }

  function onPageChange(page: number) {
    const target = Math.max(1, Math.min(metaData.value.totalPages, page));
    if (target === listPage.page.value) return;
    listPage.clear();
    listPage.page.value = target;
  }

  function goDetail(deviceId: string, _deviceName: string) {
    const activeContext = context.value;
    if (!activeContext || deviceId !== activeContext.deviceId) return;
    void router.push({
      name: 'CapacityDetail',
      query: {
        processId: activeContext.processId,
        deviceId: activeContext.deviceId,
      },
    });
  }

  const rowKey = (row: DailyCapacityItem) => `${row.deviceId}-${row.date}`;

  watch(selectionRevision, () => {
    listPage.clear();
    listError.value = null;
    if (listPage.page.value !== 1) {
      listPage.page.value = 1;
      return;
    }
    if (context.value) void fetchData();
  }, { flush: 'sync' });

  return {
    records: listPage.items,
    loading: listPage.loading,
    currentPage: listPage.page,
    metaData,
    dateFilter,
    processOptions,
    deviceOptions,
    selectedProcessId,
    selectedDeviceId,
    context,
    contextStatus,
    contextError,
    contextState,
    hasAuthorizedDevices,
    listError,
    totalStats,
    initialize,
    fetchData,
    selectProcess,
    selectDevice,
    resetPageAndFetch,
    clearFilters,
    onPageChange,
    goDetail,
    rowKey,
  };
}
