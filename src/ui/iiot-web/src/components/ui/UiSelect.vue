<template>
  <select
    v-bind="$attrs"
    :value="value ?? ''"
    :disabled="disabled || loading"
    :class="classes"
    @change="onChange"
  >
    <option v-if="clearable || placeholder" value="">{{ placeholder || '全部' }}</option>
    <option
      v-for="option in options"
      :key="String(option.value)"
      :value="String(option.value)"
      :disabled="option.disabled"
    >
      {{ option.label }}
    </option>
  </select>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { cn } from '../../lib/utils';
import type { UiSelectOption } from './types';

const props = withDefaults(defineProps<{
  value?: string | number | boolean | null;
  options?: UiSelectOption[];
  placeholder?: string;
  clearable?: boolean;
  filterable?: boolean;
  loading?: boolean;
  disabled?: boolean;
  class?: string;
}>(), {
  options: () => [],
});

const emit = defineEmits<{
  'update:value': [value: string | number | boolean | null];
}>();

const classes = computed(() => cn(
  'h-11 w-full rounded-[14px] border border-[var(--input)] bg-[#f7fafb] px-4 text-[14px] font-bold text-[#111827]',
  'disabled:cursor-not-allowed disabled:bg-[#eef2f4] disabled:text-[#9aa3af]',
  'focus:border-[#111827] focus:bg-white focus:outline-none focus:ring-2 focus:ring-[rgba(17,24,39,0.08)]',
  'dark:bg-[#202024] dark:text-[#f5f5f4] dark:focus:border-[#f5f5f4]',
  props.class,
));

function onChange(event: Event) {
  const raw = (event.target as HTMLSelectElement).value;
  if (raw === '') {
    emit('update:value', null);
    return;
  }

  const matched = props.options.find((option) => String(option.value) === raw);
  emit('update:value', matched?.value ?? raw);
}
</script>
