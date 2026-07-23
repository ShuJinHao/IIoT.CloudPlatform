<template>
  <UiModal v-model:show="show" preset="card" :title="title" style="width: 860px;">
    <div v-if="selectedRow" class="history-modal">
      <div class="history-summary">
        <div>
          <strong>{{ selectedRow.componentName }}</strong>
          <code>{{ selectedRow.componentCode }}</code>
        </div>
        <UiTag :type="selectedRow.kind === 'host' ? 'default' : 'info'" size="small" :bordered="false">{{ selectedRow.kindLabel }}</UiTag>
      </div>
      <UiDataTable :columns="columns" :data="versions" :row-key="rowKey">
        <template #empty>
          <EmptyState title="无其他活动版本" description="当前组件只有一个活动版本。" />
        </template>
      </UiDataTable>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">关闭</UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type { ReleaseCatalogRow, ReleaseVersionEntry } from './types';

const show = defineModel<boolean>('show', { required: true });
defineProps<{
  title: string;
  selectedRow: ReleaseCatalogRow | null;
  versions: ReleaseVersionEntry[];
  columns: UiDataTableColumn<ReleaseVersionEntry>[];
}>();

const rowKey = (row: ReleaseVersionEntry) => row.id;
</script>
