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
      <v-chart v-else class="donut__chart" :option="chartOption" autoresize />
    </div>
    <div v-if="!loadFailed" class="donut__legend">
      <div v-for="seg in segments" :key="seg.label" class="donut__legend-row">
        <span class="donut__swatch" :style="{ background: seg.color }"></span>
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
import { useTheme } from '../../composables/useTheme';

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

const { mode } = useTheme();

const total = computed(() => props.segments.reduce((sum, item) => sum + item.value, 0));

const subtitleText = computed(() =>
  props.loadFailed ? '设备状态加载失败' : `${total.value} 台设备总览`,
);

const onlineRate = computed(() => {
  if (total.value === 0) return 0;
  const online = props.segments.find((item) => item.label === '在线' || item.label === 'Online');
  if (!online) return 0;
  return Math.round((online.value / total.value) * 1000) / 10;
});

const chartOption = computed(() => {
  const isDark = mode.value === 'dark';

  return {
    tooltip: {
      trigger: 'item',
      backgroundColor: isDark ? '#18181b' : '#ffffff',
      borderColor: isDark ? 'rgba(255,255,255,0.12)' : 'rgba(17,24,39,0.08)',
      borderWidth: 1,
      textStyle: { color: isDark ? '#f5f5f4' : '#111827', fontSize: 12 },
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
              color: isDark ? '#c6f452' : '#111827',
              fontSize: 30,
              fontWeight: 800,
              lineHeight: 38,
            },
            label: {
              color: isDark ? '#a1a1aa' : '#8a93a3',
              fontSize: 11,
              lineHeight: 16,
            },
          },
        },
        labelLine: { show: false },
        data: props.segments.map((item) => ({
          name: item.label,
          value: item.value,
          itemStyle: {
            color: item.color,
            borderColor: isDark ? '#18181b' : '#ffffff',
            borderWidth: 3,
          },
        })),
      },
    ],
  };
});
</script>

<style scoped>
.donut__body {
  position: relative;
  height: 210px;
}

.donut__chart {
  width: 100%;
  height: 100%;
}

.donut__legend {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  margin-top: var(--space-4);
}

.donut__legend-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  color: var(--text-1);
  font-size: var(--fs-base);
}

.donut__swatch {
  width: 10px;
  height: 10px;
  flex-shrink: 0;
  border-radius: var(--radius-full);
}

.donut__legend-label {
  flex: 1;
}

.donut__legend-count {
  color: var(--text-0);
  font-family: var(--font-mono);
  font-weight: var(--fw-strong);
}

.donut__demo-tag {
  border-radius: var(--radius-full);
  background: var(--warn-soft);
  color: var(--warn);
  padding: 5px 10px;
  font-size: var(--fs-xs);
  font-weight: var(--fw-bold);
  letter-spacing: 0;
}
</style>
