import { computed, ref } from 'vue';
import { useListPage } from '../../core/list-page';
import {
  getEdgeHostDetailApi,
  getEdgeHostPagedListApi,
  getEdgeHostPlcRuntimeStatesApi,
  type EdgeHostDto,
  type EdgeHostListItemDto,
  type EdgeHostPlcRuntimeStateDto,
} from './api';
import {
  createEdgeHostColumns,
  createPlcRuntimeStateColumns,
} from './columns';
import {
  EDGE_HOST_PAGE_SIZE,
  emptyEdgeHostMetaData,
} from './types';

export function useEdgeHosts() {
  const detailLoading = ref(false);
  const runtimeLoading = ref(false);
  const metaData = ref(emptyEdgeHostMetaData());
  const selectedHost = ref<EdgeHostDto | null>(null);
  const runtimeStates = ref<EdgeHostPlcRuntimeStateDto[]>([]);
  const showPlcDrawer = ref(false);

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

  const hostColumns = computed(() => createEdgeHostColumns({
    onOpenPlcState: openPlcDrawer,
  }));
  const runtimeColumns = computed(() => createPlcRuntimeStateColumns());

  let searchTimer: ReturnType<typeof setTimeout> | null = null;

  async function fetchList() {
    await listPage.refresh();
    if (listPage.error.value) {
      metaData.value = emptyEdgeHostMetaData();
      listPage.page.value = 1;
    }
  }

  async function initialize() {
    await fetchList();
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

  async function openPlcDrawer(host: EdgeHostListItemDto) {
    showPlcDrawer.value = true;
    detailLoading.value = true;
    runtimeLoading.value = true;
    selectedHost.value = null;
    runtimeStates.value = [];
    try {
      const [detail, states] = await Promise.all([
        getEdgeHostDetailApi(host.id),
        getEdgeHostPlcRuntimeStatesApi(host.id),
      ]);
      selectedHost.value = detail;
      runtimeStates.value = states;
    } catch {
      showPlcDrawer.value = false;
      runtimeStates.value = [];
    } finally {
      detailLoading.value = false;
      runtimeLoading.value = false;
    }
  }

  function closePlcDrawer() {
    showPlcDrawer.value = false;
    selectedHost.value = null;
    runtimeStates.value = [];
  }

  return {
    hosts: listPage.items,
    loading: listPage.loading,
    keyword,
    currentPage: listPage.page,
    metaData,
    detailLoading,
    runtimeLoading,
    selectedHost,
    runtimeStates,
    showPlcDrawer,
    hostColumns,
    runtimeColumns,
    initialize,
    fetchList,
    onSearchInput,
    onClearKeyword,
    onPageChange,
    refreshSelectedRuntimeStates,
    closePlcDrawer,
  };
}
