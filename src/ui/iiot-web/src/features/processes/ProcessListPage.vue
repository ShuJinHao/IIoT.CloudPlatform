<template>
  <NiondDataPage
    class="process-page"
    page-key="processes"
    title="工序管理"
    subtitle="定义与维护车间制造工序，作为设备、配方、员工权限的核心锚点"
  >
    <template #actions>
      <UiButton v-if="canCreateProcess" type="primary" @click="openCreateModal">
        <template #icon><Plus :size="14" /></template>
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
            <template #prefix><Search :size="14" /></template>
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
        :row-key="rowKey"
      >
        <template #empty>
          <EmptyState title="未找到工序" description="当前没有工序数据或未找到匹配结果。" />
        </template>
      </UiDataTable>
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

    <ProcessFormModal
      v-model:show="showFormModal"
      :form="formData"
      :edit-target="editTarget"
      :submitting="submitting"
      @submit="submitForm"
    />
    <ProcessDeleteConfirm :dialog="confirmDialog" :submitting="submitting" />
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { Plus, Search } from 'lucide-vue-next';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import UiTag from '../../components/ui/UiTag.vue';
import { createProcessColumns } from './columns';
import ProcessDeleteConfirm from './ProcessDeleteConfirm.vue';
import ProcessFormModal from './ProcessFormModal.vue';
import { useProcesses } from './useProcesses';
import './process-page.css';

const {
  processes,
  loading,
  keyword,
  currentPage,
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
} = useProcesses();

const columns = createProcessColumns({
  canUpdate: () => canUpdateProcess.value,
  canDelete: () => canDeleteProcess.value,
  onEdit: openEditModal,
  onDelete: handleDelete,
});

onMounted(fetchList);
</script>
