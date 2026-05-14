<template>
  <div class="skeleton" :class="[`skeleton--${variant}`, { 'skeleton--animated': animated }]">
    <!-- Circle variant -->
    <div v-if="variant === 'circle'" class="skeleton__circle" :style="{ width: size, height: size }"></div>

    <!-- Rect variant -->
    <div v-else-if="variant === 'rect'" class="skeleton__rect" :style="{ width, height }"></div>

    <!-- Text lines variant -->
    <template v-else>
      <div
        v-for="i in lines"
        :key="i"
        class="skeleton__text"
        :style="{ width: i === lines ? lastLineWidth : '100%' }"
      ></div>
    </template>
  </div>
</template>

<script setup lang="ts">
withDefaults(defineProps<{
  variant?: 'text' | 'circle' | 'rect';
  lines?: number;
  lastLineWidth?: string;
  width?: string;
  height?: string;
  size?: string;
  animated?: boolean;
}>(), {
  variant: 'text',
  lines: 3,
  lastLineWidth: '60%',
  width: '100%',
  height: '100px',
  size: '48px',
  animated: true
});
</script>

<style scoped>
.skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.skeleton__circle {
  border-radius: 50%;
  background-color: var(--bg-3);
}

.skeleton__rect {
  border-radius: var(--radius-md);
  background-color: var(--bg-3);
}

.skeleton__text {
  height: 16px;
  border-radius: var(--radius-sm);
  background-color: var(--bg-3);
}

/* Shimmer Animation */
.skeleton--animated .skeleton__circle,
.skeleton--animated .skeleton__rect,
.skeleton--animated .skeleton__text {
  background: linear-gradient(
    90deg,
    var(--bg-3) 0%,
    var(--border-strong) 50%,
    var(--bg-3) 100%
  );
  background-size: 200% 100%;
  animation: skeleton-shimmer 1.5s infinite linear;
}

@keyframes skeleton-shimmer {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}
</style>
