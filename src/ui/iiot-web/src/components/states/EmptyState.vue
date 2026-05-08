<template>
  <div class="empty">
    <div class="empty__icon">
      <slot name="icon">
        <svg viewBox="0 0 64 64" width="48" height="48" fill="none" stroke="currentColor" stroke-width="2">
          <rect x="8" y="14" width="48" height="38" rx="4" />
          <path d="M16 28h32" stroke-dasharray="3 3" />
          <path d="M16 38h20" stroke-dasharray="3 3" />
        </svg>
      </slot>
    </div>
    <p class="empty__title">{{ title || '暂无数据' }}</p>
    <p v-if="description" class="empty__desc">{{ description }}</p>
    <div v-if="$slots.action" class="empty__action">
      <slot name="action" />
    </div>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  title?: string;
  description?: string;
}>();
</script>

<style scoped>
.empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: var(--space-12) var(--space-5);
  text-align: center;
}
.empty__icon {
  color: var(--text-2);
  margin-bottom: var(--space-3);
}
.empty__title {
  font-size: var(--fs-md);
  color: var(--text-1);
  margin: 0;
  font-weight: var(--fw-medium);
}
.empty__desc {
  font-size: var(--fs-sm);
  color: var(--text-2);
  margin: var(--space-2) 0 0;
  max-width: 320px;
  line-height: 1.5;
}
.empty__action {
  margin-top: var(--space-4);
}
</style>
