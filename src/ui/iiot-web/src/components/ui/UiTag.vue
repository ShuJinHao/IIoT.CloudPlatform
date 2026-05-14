<template>
  <span :class="classes">
    <slot />
  </span>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { cn } from '../../lib/utils';

const props = withDefaults(defineProps<{
  type?: 'default' | 'primary' | 'info' | 'success' | 'warning' | 'error';
  size?: 'small' | 'medium' | 'large';
  round?: boolean;
  bordered?: boolean;
  class?: string;
}>(), {
  type: 'default',
  size: 'medium',
  bordered: true,
});

const tone = computed(() => ({
  default: 'bg-[#f4f7f8] text-[#596273] border-[#e4e9ec]',
  primary: 'bg-[#eff7d5] text-[#111827] border-[#dff48a]',
  info: 'bg-[#e9f1ff] text-[#37659e] border-[#d9e8ff]',
  success: 'bg-[#e5f7ef] text-[#0a7f62] border-[#cceedd]',
  warning: 'bg-[#fff1cf] text-[#9a6500] border-[#ffe2a2]',
  error: 'bg-[#ffe7e7] text-[#b42323] border-[#ffd0d0]',
}[props.type]));

const size = computed(() => ({
  small: 'min-h-6 px-2 text-[12px]',
  medium: 'min-h-7 px-2.5 text-[13px]',
  large: 'min-h-8 px-3 text-[14px]',
}[props.size]));

const classes = computed(() => cn(
  'inline-flex items-center justify-center whitespace-nowrap font-extrabold',
  props.round ? 'rounded-full' : 'rounded-[10px]',
  props.bordered ? 'border' : 'border border-transparent',
  tone.value,
  size.value,
  props.class,
));
</script>
