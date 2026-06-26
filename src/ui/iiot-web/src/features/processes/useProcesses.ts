import { computed, reactive, ref } from 'vue';
import { useListPage } from '../../core/list-page';
import { useAuthStore } from '../../stores/auth';
import { Permissions } from '../../types/permissions';
import { notifyWarning } from '../../utils/feedback';
import {
  createProcessApi,
  deleteProcessApi,
  getProcessPagedListApi,
  updateProcessApi,
  type ProcessListItemDto,
} from './api';
import {
  emptyProcessMetaData,
  normalizeProcessPageResult,
  PROCESS_PAGE_SIZE,
  validateProcessForm,
  type ProcessConfirmDialog,
} from './types';

export function useProcesses() {
  const authStore = useAuthStore();
  const submitting = ref(false);
  const metaData = ref(emptyProcessMetaData());
  const showFormModal = ref(false);
  const editTarget = ref<ProcessListItemDto | null>(null);
  const formData = reactive({ processCode: '', processName: '' });
  const confirmDialog = reactive<ProcessConfirmDialog>({
    show: false,
    title: '',
    desc: '',
    confirmText: '',
    onConfirm: async () => {},
  });

  const listPage = useListPage<ProcessListItemDto, { keyword: string }>({
    initialFilter: { keyword: '' },
    initialPageSize: PROCESS_PAGE_SIZE,
    immediate: false,
    fetcher: async ({ page, pageSize, filter }) => {
      const raw = await getProcessPagedListApi({
        pagination: { PageNumber: page, PageSize: pageSize },
        keyword: filter.keyword || undefined,
      });
      const normalized = normalizeProcessPageResult(raw);
      metaData.value = normalized.metaData;
      return {
        items: normalized.items,
        total: normalized.metaData.totalCount,
      };
    },
  });

  const keyword = computed({
    get: () => listPage.filter.keyword,
    set: (value: string) => {
      listPage.filter.keyword = value;
    },
  });
  const canCreateProcess = computed(() =>
    authStore.hasPermission(Permissions.Process.Create),
  );
  const canUpdateProcess = computed(() =>
    authStore.hasPermission(Permissions.Process.Update),
  );
  const canDeleteProcess = computed(() =>
    authStore.hasPermission(Permissions.Process.Delete),
  );
  const rowKey = (row: ProcessListItemDto) => row.id;

  let searchTimer: ReturnType<typeof setTimeout> | null = null;

  async function fetchList() {
    await listPage.refresh();
    if (listPage.error.value) {
      metaData.value = emptyProcessMetaData();
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

  function openCreateModal() {
    editTarget.value = null;
    formData.processCode = '';
    formData.processName = '';
    showFormModal.value = true;
  }

  function openEditModal(process: ProcessListItemDto) {
    editTarget.value = process;
    formData.processCode = process.processCode;
    formData.processName = process.processName;
    showFormModal.value = true;
  }

  async function submitForm() {
    const validationMessage = validateProcessForm(formData);
    if (validationMessage) {
      notifyWarning(validationMessage);
      return;
    }
    submitting.value = true;
    try {
      if (editTarget.value) {
        await updateProcessApi(editTarget.value.id, {
          processId: editTarget.value.id,
          processCode: formData.processCode,
          processName: formData.processName,
        });
      } else {
        await createProcessApi({ ...formData });
      }
      showFormModal.value = false;
      await fetchList();
    } catch {
      /* feedback handled by http client */
    } finally {
      submitting.value = false;
    }
  }

  function handleDelete(process: ProcessListItemDto) {
    Object.assign(confirmDialog, {
      show: true,
      title: '确认删除工序',
      desc: `工序【${process.processName}（${process.processCode}）】删除后不可恢复。若该工序下仍有设备或配方挂载，删除将被拒绝。`,
      confirmText: '确认删除',
      onConfirm: async () => {
        submitting.value = true;
        try {
          await deleteProcessApi(process.id);
          confirmDialog.show = false;
          await refreshAfterMutation();
        } catch {
          /* feedback handled by http client */
        } finally {
          submitting.value = false;
        }
      },
    });
  }

  return {
    processes: listPage.items,
    loading: listPage.loading,
    keyword,
    currentPage: listPage.page,
    metaData,
    submitting,
    showFormModal,
    editTarget,
    formData,
    confirmDialog,
    canCreateProcess,
    canUpdateProcess,
    canDeleteProcess,
    fetchList,
    onSearchInput,
    onClearKeyword,
    onPageChange,
    openCreateModal,
    openEditModal,
    submitForm,
    handleDelete,
    rowKey,
  };
}
