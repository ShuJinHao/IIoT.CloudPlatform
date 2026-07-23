import { computed, ref } from 'vue';
import { useListPage } from '../../core/list-page';
import { useAuthStore } from '../../stores/auth';
import { Permissions } from '../../types/permissions';
import {
  getDeviceClientOverviewsApi,
  getDeviceClientReleaseDetailsApi,
  getEdgeHostPlcRuntimeStatesApi,
  type DeviceClientOverviewItemDto,
  type DeviceClientOverviewSortBy,
  type DeviceClientOverviewSortDirection,
  type DeviceClientReleaseDetailsDto,
  type EdgeHostPlcRuntimeStateDto,
} from './api';
import {
  DEFAULT_SORT_BY,
  DEFAULT_SORT_DIRECTION,
  OVERVIEW_PAGE_SIZE,
  resolveInlineErrorMessage,
} from './types';

interface OverviewFilter extends Record<string, unknown> {
  keyword: string;
  sortBy: DeviceClientOverviewSortBy;
  sortDirection: DeviceClientOverviewSortDirection;
}

export function useDeviceClientOverviews() {
  const authStore = useAuthStore();
  const canViewPlcDetails = computed(() => authStore.hasPermission(Permissions.EdgeHost.Read));
  const canViewReleaseDetails = computed(() => authStore.hasPermission(Permissions.ClientRelease.Read));
  const canViewAnyDetails = computed(
    () => canViewPlcDetails.value || canViewReleaseDetails.value,
  );

  const listPage = useListPage<DeviceClientOverviewItemDto, OverviewFilter>({
    initialFilter: { keyword: '', sortBy: DEFAULT_SORT_BY, sortDirection: DEFAULT_SORT_DIRECTION },
    initialPageSize: OVERVIEW_PAGE_SIZE,
    immediate: false,
    fetcher: async ({ page, pageSize, filter }) => {
      const response = await getDeviceClientOverviewsApi({
        pageNumber: page,
        pageSize,
        keyword: filter.keyword,
        sortBy: filter.sortBy,
        sortDirection: filter.sortDirection,
      });
      return { items: response.items, total: response.metaData.totalCount };
    },
  });

  const keyword = computed({
    get: () => listPage.filter.keyword,
    set: (value: string) => {
      listPage.filter.keyword = value;
    },
  });
  const sortBy = computed(() => listPage.filter.sortBy);
  const sortDirection = computed(() => listPage.filter.sortDirection);

  let searchTimer: ReturnType<typeof setTimeout> | null = null;

  async function refresh() {
    await listPage.refresh();
  }

  function onSearchInput() {
    if (searchTimer) clearTimeout(searchTimer);
    searchTimer = setTimeout(() => {
      listPage.page.value = 1;
      void listPage.refresh();
    }, 400);
  }

  function onClearKeyword() {
    keyword.value = '';
    listPage.page.value = 1;
    void listPage.refresh();
  }

  function toggleSort(key: DeviceClientOverviewSortBy) {
    if (listPage.filter.sortBy === key) {
      listPage.filter.sortDirection = listPage.filter.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      listPage.filter.sortBy = key;
      listPage.filter.sortDirection = 'asc';
    }
    listPage.page.value = 1;
    void listPage.refresh();
  }

  // ===== 详情抽屉：PLC 与版本详情权限独立、请求独立、失败互不影响 =====
  const showDetailDrawer = ref(false);
  const selectedDevice = ref<DeviceClientOverviewItemDto | null>(null);
  const plcStates = ref<EdgeHostPlcRuntimeStateDto[]>([]);
  const plcLoading = ref(false);
  const plcError = ref<string | null>(null);
  const releaseDetails = ref<DeviceClientReleaseDetailsDto | null>(null);
  const releaseLoading = ref(false);
  const releaseError = ref<string | null>(null);
  let plcRequestGeneration = 0;
  let releaseRequestGeneration = 0;

  function invalidateDetailRequests() {
    plcRequestGeneration += 1;
    releaseRequestGeneration += 1;
  }

  function resetDetailState() {
    plcStates.value = [];
    plcLoading.value = false;
    plcError.value = null;
    releaseDetails.value = null;
    releaseLoading.value = false;
    releaseError.value = null;
  }

  async function fetchPlcStates(deviceId: string) {
    const requestGeneration = ++plcRequestGeneration;
    const isCurrentRequest = () =>
      requestGeneration === plcRequestGeneration
      && showDetailDrawer.value
      && selectedDevice.value?.deviceId === deviceId
      && canViewPlcDetails.value;

    plcLoading.value = true;
    plcError.value = null;
    try {
      const nextStates = await getEdgeHostPlcRuntimeStatesApi(deviceId);
      if (!isCurrentRequest()) return;
      plcStates.value = nextStates;
    } catch (error) {
      const message = await resolveInlineErrorMessage(error, 'PLC 状态加载失败，请重试。');
      if (!isCurrentRequest()) return;
      plcStates.value = [];
      plcError.value = message;
    } finally {
      if (isCurrentRequest()) {
        plcLoading.value = false;
      }
    }
  }

  async function fetchReleaseDetails(deviceId: string) {
    const requestGeneration = ++releaseRequestGeneration;
    const isCurrentRequest = () =>
      requestGeneration === releaseRequestGeneration
      && showDetailDrawer.value
      && selectedDevice.value?.deviceId === deviceId
      && canViewReleaseDetails.value;

    releaseLoading.value = true;
    releaseError.value = null;
    try {
      const nextDetails = await getDeviceClientReleaseDetailsApi(deviceId);
      if (!isCurrentRequest()) return;
      releaseDetails.value = nextDetails;
    } catch (error) {
      const message = await resolveInlineErrorMessage(error, '版本与升级详情加载失败，请重试。');
      if (!isCurrentRequest()) return;
      releaseDetails.value = null;
      releaseError.value = message;
    } finally {
      if (isCurrentRequest()) {
        releaseLoading.value = false;
      }
    }
  }

  function openDetailDrawer(row: DeviceClientOverviewItemDto) {
    if (!canViewAnyDetails.value) return;

    invalidateDetailRequests();
    resetDetailState();
    selectedDevice.value = row;
    showDetailDrawer.value = true;
    // 无对应权限时既不请求，也不渲染占位详情。
    if (canViewPlcDetails.value) {
      void fetchPlcStates(row.deviceId);
    }
    if (canViewReleaseDetails.value) {
      void fetchReleaseDetails(row.deviceId);
    }
  }

  function retryPlcStates() {
    if (selectedDevice.value && canViewPlcDetails.value) {
      void fetchPlcStates(selectedDevice.value.deviceId);
    }
  }

  function retryReleaseDetails() {
    if (selectedDevice.value && canViewReleaseDetails.value) {
      void fetchReleaseDetails(selectedDevice.value.deviceId);
    }
  }

  function closeDetailDrawer() {
    invalidateDetailRequests();
    showDetailDrawer.value = false;
    selectedDevice.value = null;
    resetDetailState();
  }

  return {
    items: listPage.items,
    total: listPage.total,
    page: listPage.page,
    pageSize: listPage.pageSize,
    totalPages: listPage.totalPages,
    loading: listPage.loading,
    error: listPage.error,
    isEmpty: listPage.isEmpty,
    keyword,
    sortBy,
    sortDirection,
    canViewPlcDetails,
    canViewReleaseDetails,
    canViewAnyDetails,
    showDetailDrawer,
    selectedDevice,
    plcStates,
    plcLoading,
    plcError,
    releaseDetails,
    releaseLoading,
    releaseError,
    refresh,
    onSearchInput,
    onClearKeyword,
    toggleSort,
    gotoPage: listPage.gotoPage,
    openDetailDrawer,
    retryPlcStates,
    retryReleaseDetails,
    closeDetailDrawer,
  };
}
