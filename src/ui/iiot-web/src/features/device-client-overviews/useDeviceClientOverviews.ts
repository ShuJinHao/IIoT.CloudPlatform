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

  async function fetchPlcStates(deviceId: string) {
    plcLoading.value = true;
    plcError.value = null;
    try {
      plcStates.value = await getEdgeHostPlcRuntimeStatesApi(deviceId);
    } catch (error) {
      plcStates.value = [];
      plcError.value = await resolveInlineErrorMessage(error, 'PLC 状态加载失败，请重试。');
    } finally {
      plcLoading.value = false;
    }
  }

  async function fetchReleaseDetails(deviceId: string) {
    releaseLoading.value = true;
    releaseError.value = null;
    try {
      releaseDetails.value = await getDeviceClientReleaseDetailsApi(deviceId);
    } catch (error) {
      releaseDetails.value = null;
      releaseError.value = await resolveInlineErrorMessage(error, '版本与升级详情加载失败，请重试。');
    } finally {
      releaseLoading.value = false;
    }
  }

  function openDetailDrawer(row: DeviceClientOverviewItemDto) {
    selectedDevice.value = row;
    plcStates.value = [];
    plcError.value = null;
    releaseDetails.value = null;
    releaseError.value = null;
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
    showDetailDrawer.value = false;
    selectedDevice.value = null;
    plcStates.value = [];
    plcError.value = null;
    releaseDetails.value = null;
    releaseError.value = null;
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
