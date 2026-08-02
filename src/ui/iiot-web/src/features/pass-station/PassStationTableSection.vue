<template>
  <NiondTableCard class="passstation-page__table-card">
    <EmptyState v-if="error && !loading" title="过站记录加载失败" :description="error">
      <template #action>
        <UiButton secondary size="small" @click="$emit('retry')">重试</UiButton>
      </template>
    </EmptyState>
    <div v-else-if="!searched && !loading" class="hint-empty">
      <EmptyState title="请填写查询条件后执行查询" description="未查询前不显示数据，避免误展示无关记录。" />
    </div>
    <UiDataTable v-else class="passstation-page__table" :columns="columns" :data="records" :loading="loading" :bordered="false" :single-line="false" :row-key="rowKey" :row-props="rowProps" size="small">
      <template #empty>
        <EmptyState title="当前设备暂无过站数据" description="所选设备与查询条件下没有过站记录。" />
      </template>
    </UiDataTable>
    <div v-if="metaData.totalPages > 1" class="pagination-wrap">
      <UiPagination :page="currentPage" :page-count="metaData.totalPages" :item-count="metaData.totalCount" :page-size="pageSize" show-quick-jumper @update:page="$emit('pageChange', $event)" />
    </div>
  </NiondTableCard>
</template>

<script setup lang="ts">
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type { PagedMetaData } from '../../core/types/pagination';
import type { PassStationListItemDto } from './api';

defineEmits<{ pageChange: [page: number]; retry: [] }>();
defineProps<{
  searched: boolean;
  loading: boolean;
  columns: UiDataTableColumn<PassStationListItemDto>[];
  records: PassStationListItemDto[];
  rowKey: (row: PassStationListItemDto) => string;
  rowProps: (row: PassStationListItemDto) => Record<string, unknown>;
  metaData: PagedMetaData;
  currentPage: number;
  pageSize: number;
  error: string | null;
}>();
</script>
