<template>
  <svg
    class="spark"
    :viewBox="`0 0 100 ${height}`"
    preserveAspectRatio="none"
    :style="{ height: `${height}px` }"
    role="img"
    aria-label="趋势迷你图"
  >
    <defs>
      <linearGradient :id="gradId" x1="0" y1="0" x2="0" y2="1">
        <stop offset="0%" :stop-color="color" stop-opacity="0.3" />
        <stop offset="100%" :stop-color="color" stop-opacity="0" />
      </linearGradient>
    </defs>
    <path v-if="areaPath" :fill="`url(#${gradId})`" :d="areaPath" />
    <path
      v-if="linePath"
      fill="none"
      :stroke="color"
      :stroke-width="strokeWidth"
      stroke-linecap="round"
      stroke-linejoin="round"
      :d="linePath"
    />
  </svg>
</template>

<script setup lang="ts">
import { computed } from 'vue';

const props = withDefaults(
  defineProps<{
    points: number[];
    color?: string;
    strokeWidth?: number;
    height?: number;
  }>(),
  {
    color: 'var(--brand)',
    strokeWidth: 1.5,
    height: 36,
  },
);

// 唯一 ID 防多个 spark 渐变冲突
const gradId = `spark-grad-${Math.random().toString(36).slice(2, 9)}`;

const normalized = computed(() => {
  if (!props.points.length) return [];
  const min = Math.min(...props.points);
  const max = Math.max(...props.points);
  const range = max - min || 1;
  const padding = props.height * 0.1;
  const usable = props.height - padding * 2;
  const denom = Math.max(1, props.points.length - 1);
  return props.points.map((v, i) => {
    const x = (i / denom) * 100;
    const y = props.height - padding - ((v - min) / range) * usable;
    return { x, y };
  });
});

const linePath = computed(() => {
  if (!normalized.value.length) return '';
  return normalized.value
    .map((p, i) => `${i === 0 ? 'M' : 'L'} ${p.x.toFixed(2)},${p.y.toFixed(2)}`)
    .join(' ');
});

const areaPath = computed(() => {
  const items = normalized.value;
  const first = items[0];
  const last = items[items.length - 1];
  if (!first || !last) return '';
  return `${linePath.value} L ${last.x.toFixed(2)},${props.height} L ${first.x.toFixed(2)},${props.height} Z`;
});
</script>

<style scoped>
.spark {
  display: block;
  width: 100%;
}
</style>
