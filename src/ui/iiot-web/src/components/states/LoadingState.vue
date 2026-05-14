<template>
  <div :class="['loading', `loading--${variant}`]">
    <div
      v-for="i in rows"
      :key="i"
      class="loading__bar"
      :style="{ width: `${barWidth(i)}%` }"
    />
  </div>
</template>

<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    rows?: number;
    variant?: 'list' | 'card' | 'inline';
  }>(),
  {
    rows: 3,
    variant: 'list',
  },
);

const widths = [100, 75, 90, 60, 85, 70];
function barWidth(i: number): number {
  return widths[(i - 1) % widths.length] ?? 100;
}
</script>

<style scoped>
.loading {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: var(--space-3) 0;
  animation: fadeIn var(--motion-slow) ease forwards;
}
.loading--card {
  padding: var(--space-5);
}
.loading--inline {
  flex-direction: row;
}
.loading__bar {
  height: 12px;
  /* 使用 CSS 变量以自适应暗色/亮色模式 */
  background: linear-gradient(
    90deg,
    var(--bg-3) 0%,
    var(--border-strong) 50%,
    var(--bg-3) 100%
  );
  background-size: 200% 100%;
  animation: shimmer 1.5s infinite linear;
  border-radius: var(--radius-sm);
}
.loading--inline .loading__bar {
  flex: 1;
}

@keyframes shimmer {
  0%   { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}

@keyframes fadeIn {
  from { opacity: 0; }
  to   { opacity: 1; }
}
</style>
