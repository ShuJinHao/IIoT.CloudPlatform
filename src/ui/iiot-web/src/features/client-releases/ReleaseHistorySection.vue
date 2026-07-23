<template>
  <NiondTableCard>
    <div class="table-heading">
      <div>
        <h2>历史版本</h2>
        <p>已归档、已删除和删除失败的版本；独立于上方活动 catalog</p>
      </div>
    </div>
    <div class="history-body">
      <div v-if="loading" class="history-loading">加载中…</div>
      <div v-else-if="error" class="history-error" role="alert">
        <EmptyState title="历史版本加载失败" :description="error.message || '请稍后重试。'" />
        <UiButton size="small" secondary @click="$emit('retry')">重试</UiButton>
      </div>
      <template v-else-if="items.length > 0">
        <ReleaseHistoryList :items="items" />
        <UiPagination
          class="history-pagination"
          :page="page"
          :page-count="pageCount"
          :item-count="total"
          :page-size="pageSize"
          @update:page="$emit('update:page', $event)"
        />
      </template>
      <EmptyState v-else title="暂无历史版本" description="当前条件下没有已归档、已删除或删除失败的版本。" />
    </div>
  </NiondTableCard>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import type { ClientReleaseHistoryComponentDto } from './api';
import ReleaseHistoryList from './ReleaseHistoryList.vue';

const props = defineProps<{
  items: ClientReleaseHistoryComponentDto[];
  total: number;
  page: number;
  pageSize: number;
  loading: boolean;
  error?: Error | null;
}>();

defineEmits<{
  'update:page': [value: number];
  retry: [];
}>();

const pageCount = computed(() => Math.max(1, Math.ceil(props.total / props.pageSize)));
</script>
