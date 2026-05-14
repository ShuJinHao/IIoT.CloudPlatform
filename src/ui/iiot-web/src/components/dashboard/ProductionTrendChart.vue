<template>
  <CardSurface title="产能趋势" :subtitle="subtitleText">
    <template #header>
      <div class="trend__head">
        <span v-if="isDemo" class="trend__demo-tag">演示数据</span>
        <span v-if="showFreshStatus" class="trend__live">
          <StatusLed status="success" />
          <span>最新</span>
        </span>
      </div>
    </template>
    <div class="trend__body">
      <LoadingState v-if="loading" variant="card" :rows="4" />
      <EmptyState
        v-else-if="!hours || hours.length === 0"
        title="暂无产能"
        description="当前日期和权限范围内暂无产能记录。"
      />
      <v-chart v-else class="trend__chart" :option="chartOption" autoresize />
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
import StatusLed from '../feedback/StatusLed.vue';
import { useTheme } from '../../composables/useTheme';

interface HourPoint {
  label: string;
  value: number;
}

const props = withDefaults(
  defineProps<{
    hours?: HourPoint[];
    loading?: boolean;
    isDemo?: boolean;
    subtitle?: string;
    showFreshStatus?: boolean;
  }>(),
  {
    hours: () => [],
    loading: false,
    isDemo: false,
    subtitle: '最近 24 小时单位时段产量',
    showFreshStatus: true,
  },
);

const { mode } = useTheme();
const subtitleText = computed(() => props.subtitle);

const chartOption = computed(() => {
  const isDark = mode.value === 'dark';
  const primary = isDark ? '#c6f452' : '#c8bbf0';
  const secondary = isDark ? '#5eead4' : '#8bd7ad';
  const gridColor = isDark ? 'rgba(255,255,255,0.08)' : 'rgba(17,24,39,0.07)';
  const textColor = isDark ? '#c4c4ca' : '#596273';
  const xAxis = props.hours.map((h) => h.label);
  const data = props.hours.map((h) => h.value);

  return {
    grid: { left: 42, right: 14, top: 16, bottom: 32 },
    tooltip: {
      trigger: 'axis',
      backgroundColor: isDark ? '#18181b' : '#ffffff',
      borderColor: isDark ? 'rgba(255,255,255,0.12)' : 'rgba(17,24,39,0.08)',
      borderWidth: 1,
      textStyle: { color: isDark ? '#f5f5f4' : '#111827', fontSize: 12 },
    },
    xAxis: {
      type: 'category',
      data: xAxis,
      axisLine: { lineStyle: { color: gridColor } },
      axisLabel: { color: textColor, fontSize: 11 },
      axisTick: { show: false },
    },
    yAxis: {
      type: 'value',
      splitLine: { lineStyle: { color: gridColor } },
      axisLine: { show: false },
      axisTick: { show: false },
      axisLabel: { color: textColor, fontSize: 11 },
    },
    series: [
      {
        name: '产量',
        type: 'bar',
        data,
        barWidth: 18,
        itemStyle: {
          borderRadius: [999, 999, 6, 6],
          color: {
            type: 'linear',
            x: 0,
            y: 0,
            x2: 0,
            y2: 1,
            colorStops: [
              { offset: 0, color: primary },
              { offset: 1, color: secondary },
            ],
          },
        },
      },
    ],
  };
});
</script>

<style scoped>
.trend__head {
  display: flex;
  align-items: center;
  gap: var(--space-3);
}

.trend__demo-tag,
.trend__live {
  display: inline-flex;
  align-items: center;
  border-radius: var(--radius-full);
  padding: 5px 10px;
  font-size: var(--fs-xs);
  font-weight: var(--fw-bold);
  letter-spacing: 0;
}

.trend__demo-tag {
  background: var(--warn-soft);
  color: var(--warn);
}

.trend__live {
  gap: var(--space-2);
  background: var(--success-soft);
  color: var(--success);
}

.trend__body {
  position: relative;
  height: 280px;
}

.trend__chart {
  width: 100%;
  height: 100%;
}
</style>
