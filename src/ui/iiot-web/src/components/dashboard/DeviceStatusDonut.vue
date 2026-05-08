<template>
  <CardSurface title="设备状态" :subtitle="subtitleText">
    <template #header>
      <span v-if="isDemo" class="donut__demo-tag">演示数据</span>
    </template>
    <div class="donut__body">
      <LoadingState v-if="loading" variant="card" :rows="3" />
      <EmptyState
        v-else-if="loadFailed"
        title="数据加载失败"
        description="无法读取当前权限范围内的设备状态。"
      />
      <v-chart
        v-else
        class="donut__chart"
        :option="chartOption"
        autoresize
      />
    </div>
    <div v-if="!loadFailed" class="donut__legend">
      <div
        v-for="seg in segments"
        :key="seg.label"
        class="donut__legend-row"
      >
        <span
          class="donut__swatch"
          :style="{ background: seg.color, boxShadow: `0 0 8px ${seg.color}` }"
        ></span>
        <span class="donut__legend-label">{{ seg.label }}</span>
        <span class="donut__legend-count">{{ seg.value }}</span>
      </div>
    </div>
  </CardSurface>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import VChart from 'vue-echarts';
import '../charts/echartsSetup';
import CardSurface from '../layout/CardSurface.vue';
import LoadingState from '../states/LoadingState.vue';
import EmptyState from '../states/EmptyState.vue';

interface Segment {
  label: string;
  value: number;
  color: string;
}

const props = defineProps<{
  segments: Segment[];
  loading?: boolean;
  isDemo?: boolean;
  loadFailed?: boolean;
}>();

const total = computed(() =>
  props.segments.reduce((s, x) => s + x.value, 0),
);
const subtitleText = computed(() =>
  props.loadFailed ? '设备状态加载失败' : `${total.value} 台设备总览`,
);

const onlineRate = computed(() => {
  if (total.value === 0) return 0;
  const online = props.segments.find((s) => s.label === '在线');
  if (!online) return 0;
  return Math.round((online.value / total.value) * 1000) / 10;
});

const chartOption = computed(() => ({
  tooltip: {
    trigger: 'item',
    backgroundColor: 'rgba(255, 255, 255, 0.98)',
    borderColor: 'rgba(15, 23, 42, 0.08)',
    borderWidth: 1,
    extraCssText: 'box-shadow: 0 4px 16px rgba(15, 23, 42, 0.08);',
    textStyle: {
      color: '#1a1d29',
      fontFamily: "'Inter', sans-serif",
      fontSize: 12,
    },
  },
  series: [
    {
      type: 'pie',
      radius: ['62%', '88%'],
      avoidLabelOverlap: false,
      label: {
        show: true,
        position: 'center',
        formatter: () => `{rate|${onlineRate.value}%}\n{label|在线率}`,
        rich: {
          rate: {
            fontSize: 30,
            fontWeight: 700,
            color: '#0891b2',
            fontFamily: "'Inter', sans-serif",
            lineHeight: 38,
          },
          label: {
            fontSize: 11,
            color: '#9ba3b4',
            fontFamily: "'Inter', sans-serif",
            lineHeight: 16,
          },
        },
      },
      labelLine: { show: false },
      data: props.segments.map((s) => ({
        name: s.label,
        value: s.value,
        itemStyle: {
          color: s.color,
          borderColor: '#ffffff',
          borderWidth: 2,
        },
      })),
    },
  ],
}));
</script>

<style scoped>
.donut__body {
  height: 200px;
  position: relative;
}
.donut__chart {
  width: 100%;
  height: 100%;
}
.donut__legend {
  margin-top: var(--space-3);
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.donut__legend-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  font-size: var(--fs-base);
  color: var(--text-1);
}
.donut__swatch {
  width: 10px;
  height: 10px;
  border-radius: 2px;
  flex-shrink: 0;
}
.donut__legend-label {
  flex: 1;
}
.donut__legend-count {
  font-family: var(--font-mono);
  color: var(--text-0);
  font-weight: var(--fw-semibold);
}
.donut__demo-tag {
  font-family: var(--font-mono);
  font-size: var(--fs-xs);
  color: var(--warn);
  background: var(--warn-soft);
  padding: 3px 8px;
  border-radius: var(--radius-sm);
  letter-spacing: 0;
}
</style>
