<template>
  <div class="stat-card">
    <div class="stat-card__label">{{ label }}</div>
    <div class="stat-card__number" :class="`stat-card__number--${accent}`">
      <span class="stat-card__value">{{ value }}</span>
      <span v-if="unit" class="stat-card__unit">{{ unit }}</span>
    </div>
    <div
      v-if="delta !== undefined"
      class="stat-card__delta"
      :class="deltaClass"
    >
      <span class="stat-card__delta-arrow">{{ deltaArrow }}</span>
      <span>{{ Math.abs(delta).toFixed(1) }}%</span>
      <span v-if="deltaSuffix" class="stat-card__delta-suffix">{{ deltaSuffix }}</span>
    </div>
    <SparkLine
      v-if="trend && trend.length > 0"
      class="stat-card__spark"
      :points="trend"
      :color="sparkColor"
    />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import SparkLine from './SparkLine.vue';

type Accent = 'brand' | 'success' | 'warn' | 'error' | 'info';

const props = withDefaults(
  defineProps<{
    label: string;
    value: string | number;
    unit?: string;
    delta?: number;
    deltaSuffix?: string;
    trend?: number[];
    accent?: Accent;
  }>(),
  {
    accent: 'brand',
    deltaSuffix: '较昨日',
  },
);

const deltaClass = computed(() => {
  if (props.delta === undefined) return '';
  return props.delta >= 0 ? 'stat-card__delta--up' : 'stat-card__delta--down';
});

const deltaArrow = computed(() => {
  if (props.delta === undefined) return '';
  return props.delta >= 0 ? '↑' : '↓';
});

const sparkColor = computed(() => {
  switch (props.accent) {
    case 'success': return 'var(--success)';
    case 'warn':    return 'var(--warn)';
    case 'error':   return 'var(--error)';
    case 'info':    return 'var(--info)';
    default:        return 'var(--brand)';
  }
});
</script>

<style scoped>
.stat-card {
  background: var(--bg-2);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: var(--space-5);
  transition: border-color var(--motion-base);
}
.stat-card:hover {
  border-color: var(--border-strong);
}
.stat-card__label {
  font-size: var(--fs-base);
  color: var(--text-1);
  margin-bottom: var(--space-3);
  font-weight: var(--fw-medium);
}
.stat-card__number {
  font-family: var(--font-mono);
  font-size: var(--fs-3xl);
  font-weight: var(--fw-bold);
  letter-spacing: 0;
  line-height: 1.05;
  color: var(--text-0);
}
.stat-card__number--brand   { color: var(--brand); }
.stat-card__number--success { color: var(--success); }
.stat-card__number--warn    { color: var(--warn); }
.stat-card__number--error   { color: var(--error); }
.stat-card__number--info    { color: var(--info); }
.stat-card__unit {
  font-size: var(--fs-md);
  color: var(--text-1);
  margin-left: var(--space-1);
  font-weight: var(--fw-medium);
}
.stat-card__delta {
  margin-top: var(--space-2);
  font-size: var(--fs-sm);
  font-weight: var(--fw-semibold);
  display: flex;
  align-items: center;
  gap: var(--space-2);
  font-family: var(--font-mono);
}
.stat-card__delta--up   { color: var(--success); }
.stat-card__delta--down { color: var(--error); }
.stat-card__delta-arrow {
  display: inline-block;
  line-height: 1;
}
.stat-card__delta-suffix {
  font-weight: var(--fw-regular);
  color: var(--text-2);
  font-family: var(--font-sans);
}
.stat-card__spark {
  margin-top: var(--space-3);
}
</style>
