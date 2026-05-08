<template>
  <section class="surface">
    <header
      v-if="title || subtitle || $slots.header"
      class="surface__head"
    >
      <div v-if="title || subtitle" class="surface__titles">
        <h3 v-if="title" class="surface__title">{{ title }}</h3>
        <p v-if="subtitle" class="surface__subtitle">{{ subtitle }}</p>
      </div>
      <div v-if="$slots.header" class="surface__head-extra">
        <slot name="header" />
      </div>
    </header>
    <div :class="['surface__body', { 'surface__body--no-pad': noPadding }]">
      <slot />
    </div>
  </section>
</template>

<script setup lang="ts">
withDefaults(
  defineProps<{
    title?: string;
    subtitle?: string;
    noPadding?: boolean;
  }>(),
  { noPadding: false },
);
</script>

<style scoped>
.surface {
  background: var(--bg-2);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  overflow: hidden;
  transition: border-color var(--motion-base);
}
.surface:hover {
  border-color: var(--border-strong);
}
.surface__head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--space-4);
  padding: var(--space-5) var(--space-5) 0;
}
.surface__title {
  font-size: var(--fs-lg);
  font-weight: var(--fw-semibold);
  margin: 0;
  letter-spacing: -0.2px;
  color: var(--text-0);
}
.surface__subtitle {
  font-size: var(--fs-sm);
  color: var(--text-1);
  margin: var(--space-1) 0 0;
}
.surface__body {
  padding: var(--space-5);
}
.surface__body--no-pad {
  padding: 0;
}
</style>
