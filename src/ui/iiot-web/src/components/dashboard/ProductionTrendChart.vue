<template>
  <CardSurface title="产能趋势" :subtitle="subtitleText">
    <template #header>
      <div class="trend__head">
        <span v-if="isDemo" class="trend__demo-tag">演示数据</span>
        <span class="trend__live">
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
      <v-chart
        v-else
        class="trend__chart"
        :option="chartOption"
        autoresize
      />
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
  }>(),
  {
    hours: () => [],
    loading: false,
    isDemo: false,
    subtitle: '最近 24 小时单位时段产量',
  },
);

const subtitleText = computed(() => props.subtitle);

const chartOption = computed(() => {
  const xAxis = props.hours.map((h) => h.label);
  const data = props.hours.map((h) => h.value);
  return {
    grid: { left: 44, right: 16, top: 16, bottom: 32 },
    tooltip: {
      trigger: 'axis',
      backgroundColor: 'rgba(255, 255, 255, 0.98)',
      borderColor: 'rgba(15, 23, 42, 0.08)',
      borderWidth: 1,
      extraCssText: 'box-shadow: 0 4px 16px rgba(15, 23, 42, 0.08);',
      textStyle: {
        color: '#1a1d29',
        fontFamily: "'Inter', sans-serif",
        fontSize: 12,
      },
      axisPointer: {
        type: 'line',
        lineStyle: { color: 'rgba(8, 145, 178, 0.5)', type: 'dashed' },
      },
    },
    xAxis: {
      type: 'category',
      data: xAxis,
      boundaryGap: false,
      axisLine: { lineStyle: { color: 'rgba(15, 23, 42, 0.08)' } },
      axisLabel: {
        color: '#6b7384',
        fontFamily: "'JetBrains Mono', monospace",
        fontSize: 11,
        interval: Math.max(0, Math.floor(xAxis.length / 8) - 1),
      },
      axisTick: { show: false },
    },
    yAxis: {
      type: 'value',
      splitLine: { lineStyle: { color: 'rgba(15, 23, 42, 0.05)' } },
      axisLine: { show: false },
      axisTick: { show: false },
      axisLabel: {
        color: '#6b7384',
        fontFamily: "'JetBrains Mono', monospace",
        fontSize: 11,
      },
    },
    series: [
      {
        name: '产量',
        type: 'line',
        data,
        smooth: true,
        symbol: 'circle',
        symbolSize: 0,
        lineStyle: {
          color: '#0891b2',
          width: 2.5,
        },
        itemStyle: { color: '#0891b2' },
        emphasis: {
          itemStyle: { color: '#0891b2', borderColor: '#ffffff', borderWidth: 2 },
        },
        areaStyle: {
          color: {
            type: 'linear',
            x: 0,
            y: 0,
            x2: 0,
            y2: 1,
            colorStops: [
              { offset: 0, color: 'rgba(8, 145, 178, 0.20)' },
              { offset: 1, color: 'rgba(8, 145, 178, 0)' },
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
.trend__demo-tag {
  font-family: var(--font-mono);
  font-size: var(--fs-xs);
  color: var(--warn);
  background: var(--warn-soft);
  padding: 3px 8px;
  border-radius: var(--radius-sm);
  letter-spacing: 0;
}
.trend__live {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  font-size: var(--fs-xs);
  color: var(--success);
  font-family: var(--font-mono);
  letter-spacing: 0;
  padding: 4px 8px;
  background: var(--success-soft);
  border-radius: var(--radius-sm);
}
.trend__body {
  height: 280px;
  position: relative;
}
.trend__chart {
  width: 100%;
  height: 100%;
}
</style>
