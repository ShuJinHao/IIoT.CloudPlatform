<template>
  <div class="stat-card" :class="`stat-card--${accent}`">
    <div class="stat-card__label">{{ label }}</div>
    <div class="stat-card__number">
      <span class="stat-card__value">{{ value }}</span>
      <span v-if="unit" class="stat-card__unit">{{ unit }}</span>
    </div>
    <div v-if="delta !== undefined" class="stat-card__delta" :class="deltaClass">
      <span>{{ deltaArrow }}</span>
      <strong>{{ Math.abs(delta).toFixed(1) }}%</strong>
      <small v-if="deltaSuffix">{{ deltaSuffix }}</small>
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
  return props.delta >= 0 ? '↗' : '↘';
});

const sparkColor = computed(() => {
  switch (props.accent) {
    case 'success': return 'var(--success)';
    case 'warn': return 'var(--warn)';
    case 'error': return 'var(--error)';
    case 'info': return 'var(--info)';
    default: return 'var(--brand)';
  }
});
</script>

<style scoped>
.stat-card {
  min-height: 148px;
  border: 1px solid var(--border);
  border-radius: var(--radius-xl);
  background: var(--bg-1);
  padding: var(--space-6);
  box-shadow: var(--shadow-sm);
  transition: border-color var(--motion-base) ease, box-shadow var(--motion-base) ease, transform var(--motion-base) ease;
}

.stat-card:hover {
  border-color: var(--border-strong);
  box-shadow: var(--shadow-card-hover);
  transform: translateY(-1px);
}

.stat-card__label {
  margin-bottom: var(--space-4);
  color: var(--text-1);
  font-size: var(--fs-sm);
  font-weight: var(--fw-bold);
}

.stat-card__number {
  display: flex;
  align-items: baseline;
  color: var(--text-0);
  font-family: var(--font-mono);
  letter-spacing: 0;
  line-height: 1;
}

.stat-card__value {
  font-size: var(--fs-display-1);
  font-weight: 800;
  letter-spacing: 0;
  font-feature-settings: 'tnum' on;
}

.stat-card__unit {
  margin-left: var(--space-1);
  color: var(--text-1);
  font-size: var(--fs-md);
  font-weight: var(--fw-semibold);
}

.stat-card__delta {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  margin-top: var(--space-4);
  border-radius: var(--radius-full);
  padding: 5px 9px;
  font-family: var(--font-mono);
  font-size: var(--fs-sm);
  font-weight: var(--fw-bold);
}

.stat-card__delta small {
  margin-left: 4px;
  color: var(--text-2);
  font-family: var(--font-sans);
  font-weight: var(--fw-semibold);
}

.stat-card__delta--up {
  background: var(--success-soft);
  color: var(--success);
}

.stat-card__delta--down {
  background: var(--error-soft);
  color: var(--error);
}

.stat-card__spark {
  margin-top: var(--space-4);
}
</style>
