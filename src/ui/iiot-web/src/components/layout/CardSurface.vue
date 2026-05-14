<template>
  <section
    class="surface"
    :class="[`surface--${variant}`, { 'surface--hoverable': hoverable }]"
  >
    <header v-if="title || subtitle || $slots.header" class="surface__head">
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
type SurfaceVariant = 'default' | 'elevated' | 'outline';

withDefaults(
  defineProps<{
    title?: string;
    subtitle?: string;
    noPadding?: boolean;
    variant?: SurfaceVariant;
    hoverable?: boolean;
  }>(),
  {
    noPadding: false,
    variant: 'default',
    hoverable: false,
  },
);
</script>

<style scoped>
.surface {
  overflow: hidden;
  border-radius: 24px;
  transition: border-color var(--motion-base) ease, box-shadow var(--motion-base) ease, transform var(--motion-base) ease;
}

.surface--default,
.surface--elevated {
  border: 1px solid var(--border);
  background: var(--bg-1);
  box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04), 0 12px 32px rgba(15, 23, 42, 0.05);
}

.surface--outline {
  border: 1px solid var(--border);
  background: transparent;
  box-shadow: none;
}

.surface--hoverable:hover {
  border-color: var(--border-strong);
  box-shadow: var(--shadow-card-hover);
  transform: translateY(-1px);
}

.surface__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-4);
  padding: 28px 28px 0;
}

.surface__title {
  margin: 0;
  color: var(--text-0);
  font-size: var(--fs-xl);
  font-weight: var(--fw-strong);
  letter-spacing: 0;
}

.surface__subtitle {
  margin: var(--space-1) 0 0;
  color: var(--text-1);
  font-size: var(--fs-sm);
  line-height: 1.5;
}

.surface__body {
  padding: 28px;
}

.surface__body--no-pad {
  padding: 0;
}
</style>
