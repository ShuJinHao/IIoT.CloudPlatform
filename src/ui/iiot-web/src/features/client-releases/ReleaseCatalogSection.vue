<template>
  <div class="release-stack">
    <NiondTableCard>
      <div class="table-heading">
        <div>
          <h2>版本 catalog</h2>
          <p>宿主和工序插件统一展示，其他活动版本从行内查看；归档和删除历史见下方。</p>
        </div>
      </div>
      <UiDataTable :columns="columns" :data="rows" :loading="loading" :row-key="rowKey">
        <template #empty>
          <EmptyState title="暂无版本数据" description="当前条件下未找到宿主或工序插件版本。" />
        </template>
      </UiDataTable>
    </NiondTableCard>
  </div>
</template>

<script setup lang="ts">
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type { ReleaseCatalogRow } from './types';

defineProps<{
  columns: UiDataTableColumn<ReleaseCatalogRow>[];
  rows: ReleaseCatalogRow[];
  loading: boolean;
}>();

const rowKey = (row: ReleaseCatalogRow) => row.key;
</script>
