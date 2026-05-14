<template>
  <button
    type="button"
    :class="classes"
    :aria-pressed="value ? 'true' : 'false'"
    :disabled="disabled"
    @click="emit('update:value', !value)"
  >
    <span :class="thumbClasses" />
  </button>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { cn } from '../../lib/utils';

const props = defineProps<{
  value?: boolean;
  disabled?: boolean;
  class?: string;
}>();

const emit = defineEmits<{
  'update:value': [value: boolean];
}>();

const classes = computed(() => cn(
  'relative inline-flex h-7 w-12 items-center rounded-full border border-transparent transition-colors',
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[rgba(17,24,39,0.14)] disabled:opacity-60',
  props.value ? 'bg-[#111827]' : 'bg-[#d8e0e4]',
  props.class,
));

const thumbClasses = computed(() => cn(
  'inline-block size-5 rounded-full bg-white shadow-sm transition-transform',
  props.value ? 'translate-x-6' : 'translate-x-1',
));
</script>
