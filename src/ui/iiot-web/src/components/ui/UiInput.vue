<template>
  <textarea
    v-if="type === 'textarea'"
    v-bind="$attrs"
    :value="value ?? ''"
    :placeholder="placeholder"
    :disabled="disabled"
    :class="classes"
    @input="onInput"
  />
  <input
    v-else
    v-bind="$attrs"
    :value="value ?? ''"
    :type="inputType"
    :placeholder="placeholder"
    :disabled="disabled"
    :class="classes"
    @input="onInput"
  />
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { cn } from '../../lib/utils';

const props = withDefaults(defineProps<{
  value?: string | number | null;
  type?: string;
  placeholder?: string;
  disabled?: boolean;
  clearable?: boolean;
  class?: string;
}>(), {
  type: 'text',
});

const emit = defineEmits<{
  'update:value': [value: string];
}>();

const inputType = computed(() => (props.type === 'password' ? 'password' : 'text'));

const classes = computed(() => cn(
  'w-full rounded-[14px] border border-[var(--input)] bg-[#f7fafb] px-4 text-[14px] font-semibold text-[#111827]',
  'placeholder:text-[#a0a8b5] disabled:cursor-not-allowed disabled:bg-[#eef2f4] disabled:text-[#9aa3af]',
  'focus:border-[#111827] focus:bg-white focus:outline-none focus:ring-2 focus:ring-[rgba(17,24,39,0.08)]',
  'dark:bg-[#202024] dark:text-[#f5f5f4] dark:focus:border-[#f5f5f4]',
  props.type === 'textarea' ? 'min-h-[96px] py-3 leading-6' : 'h-11',
  props.class,
));

function onInput(event: Event) {
  emit('update:value', (event.target as HTMLInputElement | HTMLTextAreaElement).value);
}
</script>
