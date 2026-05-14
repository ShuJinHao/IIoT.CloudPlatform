<template>
  <label :class="classes">
    <input
      class="ui-checkbox__input"
      type="checkbox"
      :checked="checked"
      :disabled="disabled"
      @change="onChange"
    />
    <span class="ui-checkbox__box" aria-hidden="true" />
    <span class="ui-checkbox__label"><slot /></span>
  </label>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { cn } from '../../lib/utils';

const props = defineProps<{
  checked?: boolean;
  disabled?: boolean;
  class?: string;
}>();

const emit = defineEmits<{
  'update:checked': [value: boolean];
}>();

const classes = computed(() => cn(
  'ui-checkbox inline-flex cursor-pointer items-center gap-2 rounded-[12px] px-2 py-1.5 text-[13px] font-bold text-[#596273]',
  'hover:bg-[#f4f7f8] has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-60',
  props.class,
));

function onChange(event: Event) {
  emit('update:checked', (event.target as HTMLInputElement).checked);
}
</script>

<style scoped>
.ui-checkbox__input {
  position: absolute;
  opacity: 0;
  pointer-events: none;
}

.ui-checkbox__box {
  width: 18px;
  height: 18px;
  border-radius: 6px;
  border: 1px solid var(--input);
  background: #fff;
  transition: all 0.16s ease;
}

.ui-checkbox__input:checked + .ui-checkbox__box {
  border-color: #111827;
  background: #111827;
  box-shadow: inset 0 0 0 4px #fff;
}

.ui-checkbox__input:focus-visible + .ui-checkbox__box {
  outline: 2px solid rgba(17, 24, 39, 0.14);
  outline-offset: 2px;
}
</style>
