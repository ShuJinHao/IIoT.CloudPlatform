<template>
  <CardSurface
    class="capacity-detail-page__chart-card"
    title="产能趋势图"
    :subtitle="chartSubtitle"
  >
    <div class="capacity-detail-page__chart-wrap">
      <LoadingState v-if="loading" variant="card" :rows="4" />
      <EmptyState v-else-if="rows.length === 0" title="该时段暂无产能数据" />
      <v-chart
        v-else
        class="capacity-detail-page__chart"
        :option="chartOption"
        autoresize
      />
    </div>
  </CardSurface>
</template>

<script setup lang="ts">
import '../../components/charts/echartsSetup';
import CardSurface from '../../components/layout/CardSurface.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import type { CapacityDetailRow } from './types';

defineProps<{
  loading: boolean;
  rows: CapacityDetailRow[];
  chartOption: unknown;
  chartSubtitle: string;
}>();
</script>
