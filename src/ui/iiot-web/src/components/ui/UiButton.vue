<template>
  <button
    v-bind="$attrs"
    :type="htmlType"
    :disabled="disabled || loading"
    :class="classes"
  >
    <span v-if="loading" class="ui-button__spinner" aria-hidden="true" />
    <slot />
  </button>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { cn } from '../../lib/utils';

const props = withDefaults(defineProps<{
  type?: 'default' | 'primary' | 'info' | 'success' | 'warning' | 'error';
  size?: 'tiny' | 'small' | 'medium' | 'large';
  secondary?: boolean;
  quaternary?: boolean;
  text?: boolean;
  round?: boolean;
  loading?: boolean;
  disabled?: boolean;
  htmlType?: 'button' | 'submit' | 'reset';
  class?: string;
}>(), {
  type: 'default',
  size: 'medium',
  htmlType: 'button',
});

const toneClasses = computed(() => {
  if (props.text || props.quaternary) {
    return {
      default: 'text-[#596273] hover:bg-[#f4f7f8] dark:text-[#c4c4ca] dark:hover:bg-[#202024]',
      primary: 'text-[#111827] hover:bg-[#eff7d5] dark:text-[#e7ff8a] dark:hover:bg-[#202024]',
      info: 'text-[#37659e] hover:bg-[#e9f1ff] dark:text-[#9fc4ff] dark:hover:bg-[#202024]',
      success: 'text-[#0b7f62] hover:bg-[#e8f7f1] dark:text-[#6ee7bd] dark:hover:bg-[#202024]',
      warning: 'text-[#9a6500] hover:bg-[#fff4d8] dark:text-[#fcd76f] dark:hover:bg-[#202024]',
      error: 'text-[#c24141] hover:bg-[#ffecec] dark:text-[#fca5a5] dark:hover:bg-[#202024]',
    }[props.type];
  }

  if (props.secondary) {
    return {
      default: 'bg-[#f4f7f8] text-[#111827] hover:bg-[#e9eef1] dark:bg-[#202024] dark:text-[#f5f5f4]',
      primary: 'bg-[#eaf7b4] text-[#111827] hover:bg-[#dff48a]',
      info: 'bg-[#e9f1ff] text-[#224b82] hover:bg-[#d9e8ff]',
      success: 'bg-[#e5f7ef] text-[#0a6f57] hover:bg-[#d4f2e6]',
      warning: 'bg-[#fff1cf] text-[#8a5a00] hover:bg-[#ffe4a3]',
      error: 'bg-[#ffe7e7] text-[#b42323] hover:bg-[#ffd6d6]',
    }[props.type];
  }

  return {
    default: 'bg-[#111827] text-white hover:bg-[#262f3f]',
    primary: 'bg-[var(--primary)] text-[var(--primary-foreground)] hover:bg-[#d4f77a]',
    info: 'bg-[#8fb7f4] text-[#111827] hover:bg-[#7eaaed]',
    success: 'bg-[#86dcae] text-[#113421] hover:bg-[#73d39f]',
    warning: 'bg-[#f4b63f] text-[#2d2108] hover:bg-[#eca82b]',
    error: 'bg-[#ef6b6b] text-white hover:bg-[#e85a5a]',
  }[props.type];
});

const sizeClasses = computed(() => ({
  tiny: 'h-7 px-2.5 text-[12px]',
  small: 'h-9 px-3 text-[13px]',
  medium: 'h-10 px-4 text-[14px]',
  large: 'h-12 px-5 text-[15px]',
}[props.size]));

const classes = computed(() => cn(
  'inline-flex items-center justify-center gap-2 whitespace-nowrap border-0 font-extrabold transition-colors',
  'disabled:pointer-events-none disabled:opacity-50',
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[rgba(17,24,39,0.14)]',
  props.round ? 'rounded-full' : 'rounded-[12px]',
  sizeClasses.value,
  toneClasses.value,
  props.class,
));
</script>

<style scoped>
.ui-button__spinner {
  width: 14px;
  height: 14px;
  border-radius: 999px;
  border: 2px solid currentColor;
  border-right-color: transparent;
  animation: ui-button-spin 0.8s linear infinite;
}

@keyframes ui-button-spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
