<template>
  <input
    v-bind="$attrs"
    :value="formattedValue ?? ''"
    :type="inputType"
    :disabled="disabled"
    :class="classes"
    @input="onInput"
  />
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { cn } from '../../lib/utils';

const props = withDefaults(defineProps<{
  formattedValue?: string | null;
  valueFormat?: string;
  type?: 'date' | 'datetime' | 'month' | 'year';
  disabled?: boolean;
  class?: string;
}>(), {
  type: 'date',
});

const emit = defineEmits<{
  'update:formattedValue': [value: string];
}>();

const inputType = computed(() => {
  if (props.type === 'month' || props.valueFormat === 'yyyy-MM') return 'month';
  if (props.type === 'datetime' || props.valueFormat?.includes('HH:mm')) return 'datetime-local';
  return 'date';
});

const classes = computed(() => cn(
  'h-11 w-full rounded-[14px] border border-[var(--input)] bg-[#f7fafb] px-4 text-[14px] font-bold text-[#111827]',
  'disabled:cursor-not-allowed disabled:bg-[#eef2f4] disabled:text-[#9aa3af]',
  'focus:border-[#111827] focus:bg-white focus:outline-none focus:ring-2 focus:ring-[rgba(17,24,39,0.08)]',
  'dark:bg-[#202024] dark:text-[#f5f5f4] dark:focus:border-[#f5f5f4]',
  props.class,
));

function onInput(event: Event) {
  emit('update:formattedValue', (event.target as HTMLInputElement).value);
}
</script>
