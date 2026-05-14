<template>
  <span class="badge" :class="`badge--${severity}`">
    <slot>{{ label }}</slot>
  </span>
</template>

<script setup lang="ts">
type Severity = 'error' | 'warn' | 'info' | 'success';

defineProps<{
  severity: Severity;
  label?: string;
}>();
</script>

<style scoped>
.badge {
  display: inline-block;
  font-family: var(--font-mono);
  font-size: var(--fs-xs);
  font-weight: var(--fw-bold);
  text-transform: uppercase;
  letter-spacing: 0;
  padding: 3px 7px;
  border-radius: var(--radius-sm);
  line-height: 1.2;
  white-space: nowrap;
}
.badge--error   { color: var(--error);   background: var(--error-soft); }
.badge--warn    { color: var(--warn);    background: var(--warn-soft); }
.badge--info    { color: var(--brand);   background: var(--brand-soft); }
.badge--success { color: var(--success); background: var(--success-soft); }
</style>
