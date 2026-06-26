<template>
  <NiondTableCard class="passstation-page__table-card">
    <div v-if="!searched && !loading" class="hint-empty">
      <EmptyState title="请填写查询条件后执行查询" description="未查询前不显示数据，避免误展示无关记录。" />
    </div>
    <UiDataTable v-else class="passstation-page__table" :columns="columns" :data="records" :loading="loading" :bordered="false" :single-line="false" :row-key="rowKey" :row-props="rowProps" size="small" />
    <div v-if="metaData.totalPages > 1" class="pagination-wrap">
      <UiPagination :page="currentPage" :page-count="metaData.totalPages" :item-count="metaData.totalCount" :page-size="pageSize" show-quick-jumper @update:page="$emit('pageChange', $event)" />
    </div>
  </NiondTableCard>
</template>

<script setup lang="ts">
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type { PagedMetaData } from '../../core/types/pagination';
import type { PassStationListItemDto } from './api';

defineEmits<{ pageChange: [page: number] }>();
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
}>();
</script>
