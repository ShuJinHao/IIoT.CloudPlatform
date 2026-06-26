import { computed, reactive, ref } from 'vue';
import { getAllActiveDevicesApi, type DeviceSelectDto } from '../devices/api';
import { getAllProcessesApi, type ProcessSelectDto } from '../processes/api';
import { useListPage } from '../../core/list-page';
import type { PagedMetaData } from '../../core/types/pagination';
import { useAuthStore } from '../../stores/auth';
import { Permissions } from '../../types/permissions';
import { notifyWarning } from '../../utils/feedback';
import {
  createRecipeApi,
  deleteRecipeApi,
  getRecipeDetailApi,
  getRecipePagedListApi,
  upgradeRecipeVersionApi,
} from './api';
import {
  isRecipeDetailDto,
  paramsToJsonb,
  parseParams,
  prettyJson,
  validateParams,
  type RecipeConfirmDialogState,
  type RecipeCreateForm,
  type RecipeDetailDto,
  type RecipeListItemDto,
  type RecipeParameter,
  type RecipeUpgradeForm,
} from './types';

const PAGE_SIZE = 10;

const emptyMetaData = (): PagedMetaData => ({
  totalCount: 0,
  pageSize: PAGE_SIZE,
  currentPage: 1,
  totalPages: 1,
});

export function useRecipes() {
  const authStore = useAuthStore();
  const metaData = ref<PagedMetaData>(emptyMetaData());
  const submitting = ref(false);
  const allProcesses = ref<ProcessSelectDto[]>([]);
  const allDevices = ref<DeviceSelectDto[]>([]);
  const showCreateModal = ref(false);
  const showUpgradeModal = ref(false);
  const showDetailPanel = ref(false);
  const detailLoading = ref(false);
  const detailData = ref<RecipeDetailDto | null>(null);
  const upgradeTarget = ref<RecipeListItemDto | null>(null);
  const createParams = ref<RecipeParameter[]>([]);
  const upgradeParams = ref<RecipeParameter[]>([]);
  const createForm = reactive<RecipeCreateForm>({ recipeName: '', processId: null, deviceId: null });
  const upgradeForm = reactive<RecipeUpgradeForm>({ newVersion: '' });
  const confirmDialog = reactive<RecipeConfirmDialogState>({
    show: false,
    title: '',
    desc: '',
    confirmText: '',
    onConfirm: async () => {},
  });

  const listPage = useListPage<RecipeListItemDto, { keyword: string }>({
    initialFilter: { keyword: '' },
    initialPageSize: PAGE_SIZE,
    immediate: false,
    fetcher: async ({ page, pageSize, filter }) => {
      const response = await getRecipePagedListApi({
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
  const canUpdateRecipe = computed(() => authStore.hasPermission(Permissions.Recipe.Update));
  const processNameMap = computed(() => Object.fromEntries(
    allProcesses.value.map((process) => [process.id, `${process.processCode} · ${process.processName}`]),
  ));
  const deviceNameMap = computed(() => Object.fromEntries(
    allDevices.value.map((device) => [device.id, device.deviceName]),
  ));
  const processOptions = computed(() => allProcesses.value.map((process) => ({
    label: `${process.processCode} · ${process.processName}`,
    value: process.id,
  })));
  const deviceOptions = computed(() => allDevices.value.map((device) => ({
    label: device.deviceName,
    value: device.id,
  })));
  const detailParams = computed(() => {
    if (!detailData.value) return [];
    return parseParams(detailData.value.parametersJsonb);
  });

  let searchTimer: ReturnType<typeof setTimeout> | null = null;

  async function fetchSelectData() {
    try {
      allProcesses.value = await getAllProcessesApi();
    } catch {
      allProcesses.value = [];
    }

    try {
      allDevices.value = await getAllActiveDevicesApi();
    } catch {
      allDevices.value = [];
    }
  }

  async function fetchList() {
    await listPage.refresh();
    if (listPage.error.value) {
      metaData.value = emptyMetaData();
      listPage.page.value = 1;
    }
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

  async function refreshAfterMutation() {
    await fetchList();
    if (listPage.items.value.length === 0 && listPage.page.value > 1) {
      listPage.page.value -= 1;
      await fetchList();
    }
  }

  async function initialize() {
    await Promise.all([fetchList(), fetchSelectData()]);
  }

  async function openCreateModal() {
    Object.assign(createForm, { recipeName: '', processId: null, deviceId: null });
    createParams.value = [];
    showCreateModal.value = true;
    await fetchSelectData();
  }

  async function submitCreate() {
    if (!createForm.recipeName.trim() || !createForm.processId || !createForm.deviceId) {
      notifyWarning('配方名称、归属工序和归属设备为必填项');
      return;
    }
    const validationMessage = validateParams(createParams.value);
    if (validationMessage) {
      notifyWarning(validationMessage);
      return;
    }

    submitting.value = true;
    try {
      await createRecipeApi({
        recipeName: createForm.recipeName,
        processId: createForm.processId,
        deviceId: createForm.deviceId,
        parametersJsonb: paramsToJsonb(createParams.value),
      });
      showCreateModal.value = false;
      await fetchList();
    } finally {
      submitting.value = false;
    }
  }

  async function openUpgradeModal(recipe: RecipeListItemDto) {
    upgradeTarget.value = recipe;
    upgradeForm.newVersion = '';
    upgradeParams.value = [];
    showUpgradeModal.value = true;
    try {
      const detail = await getRecipeDetailApi(recipe.id);
      upgradeParams.value = parseParams(detail.parametersJsonb || '');
    } catch (error: unknown) {
      upgradeParams.value = isRecipeDetailDto(error) ? parseParams(error.parametersJsonb) : [];
    }
  }

  async function submitUpgrade() {
    if (!upgradeTarget.value || !upgradeForm.newVersion.trim()) {
      notifyWarning('版本号不能为空');
      return;
    }
    const validationMessage = validateParams(upgradeParams.value);
    if (validationMessage) {
      notifyWarning(validationMessage);
      return;
    }

    submitting.value = true;
    try {
      await upgradeRecipeVersionApi(upgradeTarget.value.id, {
        sourceRecipeId: upgradeTarget.value.id,
        newVersion: upgradeForm.newVersion,
        parametersJsonb: paramsToJsonb(upgradeParams.value),
      });
      showUpgradeModal.value = false;
      await fetchList();
    } finally {
      submitting.value = false;
    }
  }

  async function openDetailPanel(recipe: RecipeListItemDto) {
    showDetailPanel.value = true;
    detailLoading.value = true;
    detailData.value = null;
    try {
      detailData.value = await getRecipeDetailApi(recipe.id);
    } catch (error: unknown) {
      if (isRecipeDetailDto(error)) {
        detailData.value = error;
      } else {
        showDetailPanel.value = false;
      }
    } finally {
      detailLoading.value = false;
    }
  }

  function handleDelete(recipe: RecipeListItemDto) {
    Object.assign(confirmDialog, {
      show: true,
      title: '确认永久删除配方',
      desc: `配方【${recipe.recipeName} · ${recipe.version}】将被永久删除且无法恢复，确认要删除吗？`,
      confirmText: '永久删除',
      onConfirm: async () => {
        submitting.value = true;
        try {
          await deleteRecipeApi(recipe.id);
          confirmDialog.show = false;
          await refreshAfterMutation();
        } finally {
          submitting.value = false;
        }
      },
    });
  }

  return {
    recipes: listPage.items,
    loading: listPage.loading,
    keyword,
    currentPage: listPage.page,
    metaData,
    submitting,
    allProcesses,
    allDevices,
    processNameMap,
    deviceNameMap,
    processOptions,
    deviceOptions,
    canUpdateRecipe,
    showCreateModal,
    createForm,
    createParams,
    showUpgradeModal,
    upgradeTarget,
    upgradeForm,
    upgradeParams,
    showDetailPanel,
    detailLoading,
    detailData,
    detailParams,
    confirmDialog,
    initialize,
    fetchList,
    onSearchInput,
    onClearKeyword,
    onPageChange,
    openCreateModal,
    submitCreate,
    openUpgradeModal,
    submitUpgrade,
    openDetailPanel,
    handleDelete,
    prettyJson,
  };
}
