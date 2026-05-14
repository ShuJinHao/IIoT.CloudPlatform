<template>
  <span class="led" :class="[`led--${status}`, { 'led--pulse': pulse }]">
    <span class="led__dot" />
    <span v-if="$slots.default || label" class="led__label">
      <slot>{{ label }}</slot>
    </span>
  </span>
</template>

<script setup lang="ts">
type LedStatus = 'success' | 'warn' | 'error' | 'info' | 'idle';

withDefaults(
  defineProps<{
    status: LedStatus;
    pulse?: boolean;
    label?: string;
  }>(),
  { pulse: false },
);
</script>

<style scoped>
.led {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  font-size: var(--fs-sm);
  color: var(--text-1);
  font-family: var(--font-mono);
  letter-spacing: 0;
}
.led__dot {
  display: inline-block;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}
.led--success .led__dot {
  background: var(--success);
  box-shadow: 0 0 0 2px var(--bg-1), 0 0 0 4px var(--success-soft);
}
.led--warn .led__dot {
  background: var(--warn);
  box-shadow: 0 0 0 2px var(--bg-1), 0 0 0 4px var(--warn-soft);
}
.led--error .led__dot {
  background: var(--error);
  box-shadow: 0 0 0 2px var(--bg-1), 0 0 0 4px var(--error-soft);
}
.led--info .led__dot {
  background: var(--info);
  box-shadow: 0 0 0 2px var(--bg-1), 0 0 0 4px var(--info-soft);
}
.led--idle .led__dot {
  background: var(--text-2);
  box-shadow: 0 0 0 2px var(--bg-1), 0 0 0 4px rgba(155, 163, 180, 0.2);
}
.led--pulse .led__dot   { animation: led-pulse 1.5s infinite; }
@keyframes led-pulse {
  0%, 100% { opacity: 1; transform: scale(1); }
  50%      { opacity: 0.5; transform: scale(0.85); }
}
</style>
