<template>
  <header class="page-header">
    <div class="page-header__main">
      <h1 class="page-header__title">{{ title }}</h1>
      <p v-if="subtitle" class="page-header__subtitle">{{ subtitle }}</p>
    </div>
    <div v-if="$slots.actions" class="page-header__actions">
      <slot name="actions" />
    </div>
  </header>
</template>

<script setup lang="ts">
defineProps<{
  title: string;
  subtitle?: string;
}>();
</script>

<style scoped>
.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-end;
  gap: var(--space-4);
  margin-bottom: var(--space-6);
  flex-wrap: wrap;
}
.page-header__main {
  min-width: 0;
}
.page-header__title {
  font-size: var(--fs-3xl);
  font-weight: var(--fw-bold);
  letter-spacing: -0.6px;
  margin: 0;
  color: var(--text-0);
  line-height: 1.2;
}
.page-header__subtitle {
  font-size: var(--fs-md);
  color: var(--text-1);
  margin: var(--space-1) 0 0;
}
.page-header__actions {
  display: flex;
  gap: var(--space-2);
  align-items: center;
}
</style>
