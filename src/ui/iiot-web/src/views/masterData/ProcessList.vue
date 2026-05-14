<template>
  <NiondDataPage
    class="process-page"
    page-key="processes"
    title="工序管理"
      subtitle="定义与维护车间制造工序，作为设备、配方、员工权限的核心锚点"
  >
      <template #actions>
        <UiButton
          type="primary"
          v-permission="'Process.Create'"
          @click="openCreateModal"
        >
          <template #icon>
            <svg viewBox="0 0 16 16" fill="none">
              <path d="M8 2v12M2 8h12" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>
            </svg>
          </template>
          新建工序
        </UiButton>
      </template>

    <template #toolbar>
      <NiondToolbar>
        <div class="filter-row">
          <UiInput
            v-model:value="keyword"
            placeholder="搜索工序编码或名称..."
            clearable
            size="small"
            style="max-width: 360px;"
            @input="onSearchInput"
            @keyup.enter="fetchList"
            @clear="onClearKeyword"
          >
            <template #prefix>
              <svg viewBox="0 0 16 16" width="14" height="14" fill="none">
                <circle cx="6.5" cy="6.5" r="4.5" stroke="currentColor" stroke-width="1.3"/>
                <path d="M10 10l3 3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>
              </svg>
            </template>
          </UiInput>
          <UiTag round :bordered="false" size="small">共 {{ metaData.totalCount }} 条</UiTag>
        </div>
      </NiondToolbar>
    </template>

    <NiondTableCard class="process-page__table-card">
      <UiDataTable
        class="process-page__table"
        :columns="columns"
        :data="processes"
        :loading="loading"
        :bordered="false"
        :single-line="false"
        :row-key="rowKey"
        size="small"
      />
      <div v-if="metaData.totalPages > 1" class="pagination-wrap">
        <UiPagination
          :page="currentPage"
          :page-count="metaData.totalPages"
          :item-count="metaData.totalCount"
          :page-size="10"
          show-quick-jumper
          @update:page="onPageChange"
        />
      </div>
    </NiondTableCard>

    <!-- 新建/编辑共用 modal -->
    <UiModal
      v-model:show="showFormModal"
      preset="card"
      :title="editTarget ? '编辑工序' : '新建制造工序'"
      style="width: 480px;"
      :mask-closable="false"
    >
      <div class="form-stack">
        <div class="form-field">
          <label class="form-label">工序编码 <span class="required">*</span></label>
          <UiInput
            v-model:value="formData.processCode"
            placeholder="如：Stacking、Injection"
            class="mono-input"
          />
          <p v-if="!editTarget" class="form-hint">编码全局唯一，建议使用英文标识符</p>
        </div>
        <div class="form-field">
          <label class="form-label">工序名称 <span class="required">*</span></label>
          <UiInput
            v-model:value="formData.processName"
            placeholder="如：叠片工序、注液工序"
          />
        </div>
      </div>
      <template #footer>
        <div class="modal-actions">
          <UiButton @click="showFormModal = false">取消</UiButton>
          <UiButton
            type="primary"
            :loading="submitting"
            @click="submitForm"
          >
            {{ editTarget ? '保存修改' : '确认创建' }}
          </UiButton>
        </div>
      </template>
    </UiModal>

    <!-- 删除确认 modal -->
    <UiModal
      v-model:show="confirmDialog.show"
      preset="card"
      :title="confirmDialog.title"
      style="width: 420px;"
      :mask-closable="false"
    >
      <p class="confirm-desc">{{ confirmDialog.desc }}</p>
      <template #footer>
        <div class="modal-actions">
          <UiButton @click="confirmDialog.show = false">取消</UiButton>
          <UiButton
            type="error"
            :loading="submitting"
            @click="confirmDialog.onConfirm()"
          >
            {{ confirmDialog.confirmText }}
          </UiButton>
        </div>
      </template>
    </UiModal>
  </NiondDataPage>
</template>

<script setup lang="ts">
import { ref, reactive, h, onMounted } from 'vue';
import {
  getProcessPagedListApi,
  createProcessApi,
  updateProcessApi,
  deleteProcessApi,
  type ProcessListItemDto,
  type PagedMetaData,
} from '../../api/masterData/processes';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';

const processes = ref<ProcessListItemDto[]>([]);
const loading = ref(false);
const keyword = ref('');
const currentPage = ref(1);
const metaData = ref<PagedMetaData>({
  totalCount: 0,
  pageSize: 10,
  currentPage: 1,
  totalPages: 1,
});
const submitting = ref(false);

// === 数据加载 ===
let searchTimer: ReturnType<typeof setTimeout> | null = null;
const onSearchInput = () => {
  if (searchTimer) clearTimeout(searchTimer);
  searchTimer = setTimeout(() => {
    currentPage.value = 1;
    fetchList();
  }, 400);
};

const onClearKeyword = () => {
  currentPage.value = 1;
  fetchList();
};

const fetchList = async () => {
  loading.value = true;
  try {
    const raw = (await getProcessPagedListApi({
      pagination: { PageNumber: currentPage.value, PageSize: 10 },
      keyword: keyword.value || undefined,
    })) as unknown as Record<string, unknown>;

    if (raw && raw.metaData) {
      metaData.value = raw.metaData as PagedMetaData;
      processes.value = Array.isArray(raw.items)
        ? (raw.items as ProcessListItemDto[])
        : [];
    } else if (Array.isArray(raw)) {
      processes.value = raw as ProcessListItemDto[];
    }
  } catch {
    processes.value = [];
  } finally {
    loading.value = false;
  }
};

const onPageChange = (p: number) => {
  currentPage.value = p;
  fetchList();
};

// === 表格列 ===
const columns: UiDataTableColumn<ProcessListItemDto>[] = [
  {
    title: '工序编码',
    key: 'processCode',
    width: 180,
    render(row) {
      return h('span', { class: 'cell-code' }, row.processCode);
    },
  },
  {
    title: '工序名称',
    key: 'processName',
    minWidth: 220,
    render(row) {
      return h('span', { class: 'cell-name' }, row.processName);
    },
  },
  {
    title: '工序 ID',
    key: 'id',
    width: 160,
    render(row) {
      return h(
        'span',
        { class: 'cell-id' },
        `${row.id.substring(0, 8)}…`,
      );
    },
  },
  {
    title: '操作',
    key: 'actions',
    width: 140,
    align: 'right',
    render(row) {
      return h('div', { class: 'row-actions' }, [
        h(
          UiButton,
          {
            size: 'tiny',
            type: 'primary',
            secondary: true,
            onClick: () => openEditModal(row),
          },
          { default: () => '编辑' },
        ),
        h(
          UiButton,
          {
            size: 'tiny',
            type: 'error',
            secondary: true,
            onClick: () => handleDelete(row),
          },
          { default: () => '删除' },
        ),
      ]);
    },
  },
];

const rowKey = (row: ProcessListItemDto) => row.id;

// === 新建/编辑 modal ===
const showFormModal = ref(false);
const editTarget = ref<ProcessListItemDto | null>(null);
const formData = reactive({ processCode: '', processName: '' });

const openCreateModal = () => {
  editTarget.value = null;
  formData.processCode = '';
  formData.processName = '';
  showFormModal.value = true;
};

const openEditModal = (p: ProcessListItemDto) => {
  editTarget.value = p;
  formData.processCode = p.processCode;
  formData.processName = p.processName;
  showFormModal.value = true;
};

const submitForm = async () => {
  if (!formData.processCode.trim() || !formData.processName.trim()) {
    alert('编码和名称均为必填项');
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
    fetchList();
  } catch {
    /* http 拦截器已弹错误 */
  } finally {
    submitting.value = false;
  }
};

// === 删除确认 ===
const confirmDialog = reactive({
  show: false,
  title: '',
  desc: '',
  confirmText: '',
  onConfirm: () => Promise.resolve(),
});

const handleDelete = (p: ProcessListItemDto) => {
  Object.assign(confirmDialog, {
    show: true,
    title: '确认删除工序',
    desc: `工序【${p.processName}（${p.processCode}）】删除后不可恢复。若该工序下仍有设备或配方挂载，删除将被拒绝。`,
    confirmText: '确认删除',
    onConfirm: async () => {
      submitting.value = true;
      try {
        await deleteProcessApi(p.id);
        confirmDialog.show = false;
        fetchList();
      } catch {
        /* */
      } finally {
        submitting.value = false;
      }
    },
  });
};

onMounted(() => fetchList());
</script>

<style scoped>
.process-page {
  font-family: var(--font-sans);
  color: var(--text-0);
}
.process-page__filter-card {
  margin-bottom: var(--space-4);
}
.filter-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  flex-wrap: wrap;
}
.process-page__table-card {
  /* */
}
.pagination-wrap {
  display: flex;
  justify-content: flex-end;
  padding: var(--space-4);
  border-top: 1px solid var(--border);
}

/* 表格单元 */
.process-page__table :deep(.cell-code) {
  font-family: var(--font-mono);
  font-size: var(--fs-sm);
  color: var(--brand);
  background: var(--brand-soft);
  padding: 2px 8px;
  border-radius: var(--radius-sm);
  font-weight: var(--fw-semibold);
}
.process-page__table :deep(.cell-name) {
  font-size: var(--fs-base);
  color: var(--text-0);
  font-weight: var(--fw-medium);
}
.process-page__table :deep(.cell-id) {
  font-family: var(--font-mono);
  font-size: var(--fs-xs);
  color: var(--text-2);
}
.process-page__table :deep(.row-actions) {
  display: flex;
  gap: var(--space-2);
  justify-content: flex-end;
}
.process-page__table :deep(.n-data-table-thead) {
  background: var(--bg-3);
}
.process-page__table :deep(.n-data-table-th) {
  font-size: var(--fs-xs) !important;
  font-weight: var(--fw-semibold) !important;
  color: var(--text-2) !important;
  letter-spacing: 0;
  text-transform: uppercase;
}
.process-page__table :deep(.n-data-table-tr:hover .n-data-table-td) {
  background-color: var(--bg-3) !important;
}

/* Modal 表单 */
.form-stack {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.form-field {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.form-label {
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
  color: var(--text-1);
}
.required {
  color: var(--error);
}
.form-hint {
  font-size: var(--fs-xs);
  color: var(--text-2);
  margin: 0;
}
.mono-input :deep(.n-input__input-el) {
  font-family: var(--font-mono);
}
.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
}
.confirm-desc {
  font-size: var(--fs-base);
  color: var(--text-1);
  line-height: 1.6;
  margin: 0;
}
</style>
