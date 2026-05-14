<template>
  <input
    v-bind="$attrs"
    :value="value ?? ''"
    type="number"
    :step="step"
    :disabled="disabled"
    :class="classes"
    @input="onInput"
  />
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { cn } from '../../lib/utils';

const props = withDefaults(defineProps<{
  value?: number | null;
  step?: number | string;
  disabled?: boolean;
  class?: string;
}>(), {
  step: 'any',
});

const emit = defineEmits<{
  'update:value': [value: number | null];
}>();

const classes = computed(() => cn(
  'h-10 w-full rounded-[12px] border border-[var(--input)] bg-[#f7fafb] px-3 text-[13px] font-bold text-[#111827]',
  'focus:border-[#111827] focus:bg-white focus:outline-none focus:ring-2 focus:ring-[rgba(17,24,39,0.08)]',
  props.class,
));

function onInput(event: Event) {
  const raw = (event.target as HTMLInputElement).value;
  emit('update:value', raw === '' ? null : Number(raw));
}
</script>
