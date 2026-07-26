<template>
  <CardSurface
    class="capacity-detail-page__chart-card"
    title="完工弹夹趋势"
    :subtitle="chartSubtitle"
  >
    <div class="capacity-detail-page__chart-wrap">
      <v-chart class="capacity-detail-page__chart" :option="chartOption" autoresize />
    </div>
  </CardSurface>
</template>

<script setup lang="ts">
import '../../components/charts/echartsSetup';
import VChart from 'vue-echarts';
import type { EChartsCoreOption } from 'echarts/core';
import CardSurface from '../../components/layout/CardSurface.vue';

defineProps<{
  chartOption: EChartsCoreOption;
  chartSubtitle: string;
}>();
</script>
