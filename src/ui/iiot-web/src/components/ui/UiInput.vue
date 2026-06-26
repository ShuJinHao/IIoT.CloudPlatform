<template>
  <textarea
    v-if="type === 'textarea'"
    v-bind="textareaAttrs"
    :value="value ?? ''"
    :placeholder="placeholder"
    :disabled="disabled"
    :class="textareaClasses"
    @input="onInput"
  />
  <div v-else :class="wrapperClasses" :style="rootStyle">
    <span v-if="hasPrefix" class="ui-input__prefix">
      <slot name="prefix" />
    </span>
    <input
      v-bind="inputAttrs"
      :value="value ?? ''"
      :type="inputType"
      :placeholder="placeholder"
      :disabled="disabled"
      :class="inputClasses"
      @input="onInput"
    />
    <button
      v-if="canClear"
      type="button"
      class="ui-input__clear"
      :disabled="disabled"
      aria-label="清空"
      @click="clear"
    >
      ×
    </button>
  </div>
</template>

<script setup lang="ts">
import { computed, useAttrs, useSlots } from 'vue';
import { cn } from '../../lib/utils';

defineOptions({ inheritAttrs: false });

const props = withDefaults(defineProps<{
  value?: string | number | null;
  type?: string;
  size?: 'default' | 'small';
  placeholder?: string;
  disabled?: boolean;
  clearable?: boolean;
  class?: string;
}>(), {
  type: 'text',
  size: 'default',
});

const emit = defineEmits<{
  'update:value': [value: string];
  clear: [];
}>();

const attrs = useAttrs();
const slots = useSlots();
const inputType = computed(() => (props.type === 'password' ? 'password' : 'text'));
const hasPrefix = computed(() => Boolean(slots.prefix));
const canClear = computed(() =>
  props.clearable && !props.disabled && String(props.value ?? '').length > 0,
);

const rootStyle = computed(() => attrs.style);
const inputAttrs = computed(() => {
  const { class: _class, style: _style, ...rest } = attrs;
  return rest;
});
const textareaAttrs = computed(() => {
  const { class: _class, ...rest } = attrs;
  return rest;
});

const baseClasses = computed(() => cn(
  'w-full rounded-[14px] border border-[var(--input)] bg-[#f7fafb] px-4 text-[14px] font-semibold text-[#111827]',
  'placeholder:text-[#a0a8b5] disabled:cursor-not-allowed disabled:bg-[#eef2f4] disabled:text-[#9aa3af]',
  'focus:border-[#111827] focus:bg-white focus:outline-none focus:ring-2 focus:ring-[rgba(17,24,39,0.08)]',
  'dark:bg-[#202024] dark:text-[#f5f5f4] dark:focus:border-[#f5f5f4]',
));

const wrapperClasses = computed(() => cn('relative w-full', props.class));
const inputClasses = computed(() => cn(
  baseClasses.value,
  props.size === 'small' ? 'h-10' : 'h-11',
  hasPrefix.value ? 'pl-10' : '',
  props.clearable ? 'pr-10' : '',
));
const textareaClasses = computed(() => cn(
  baseClasses.value,
  'min-h-[96px] py-3 leading-6',
  props.class,
));

function onInput(event: Event) {
  emit('update:value', (event.target as HTMLInputElement | HTMLTextAreaElement).value);
}

function clear() {
  emit('update:value', '');
  emit('clear');
}
</script>

<style scoped>
.ui-input__prefix,
.ui-input__clear {
  position: absolute;
  top: 50%;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  transform: translateY(-50%);
  color: #8a94a6;
}

.ui-input__prefix {
  left: 14px;
  pointer-events: none;
}

.ui-input__clear {
  right: 10px;
  width: 26px;
  height: 26px;
  border: 0;
  border-radius: 999px;
  background: transparent;
  font-size: 18px;
  font-weight: 700;
  line-height: 1;
  cursor: pointer;
}

.ui-input__clear:hover {
  background: rgba(17, 24, 39, 0.08);
  color: #111827;
}
</style>
