<template>
  <div class="empty">
    <div class="empty__icon-wrapper">
      <div class="empty__icon">
        <slot name="icon">
          <svg viewBox="0 0 100 100" width="92" height="92" fill="none" stroke="currentColor" stroke-width="2">
            <rect x="22" y="28" width="56" height="44" rx="12" />
            <path d="M34 42h32" stroke-dasharray="4 4" />
            <path d="M34 56h22" stroke-dasharray="4 4" />
          </svg>
        </slot>
      </div>
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
  padding: var(--space-12) var(--space-6);
  text-align: center;
}

.empty__icon-wrapper {
  display: grid;
  width: 120px;
  height: 120px;
  place-items: center;
  margin-bottom: var(--space-4);
  border: 1px solid var(--border);
  border-radius: 32px;
  background: var(--bg-3);
}

.empty__icon {
  display: flex;
  color: var(--text-2);
}

.empty__title {
  margin: 0;
  color: var(--text-0);
  font-size: var(--fs-md);
  font-weight: var(--fw-strong);
}

.empty__desc {
  max-width: 360px;
  margin: var(--space-2) 0 0;
  color: var(--text-1);
  font-size: var(--fs-sm);
  line-height: 1.55;
}

.empty__action {
  margin-top: var(--space-5);
}
</style>
