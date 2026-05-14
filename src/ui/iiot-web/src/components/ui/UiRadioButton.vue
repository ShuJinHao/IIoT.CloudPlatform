<template>
  <button type="button" :class="classes" @click="update(value)">
    <slot />
  </button>
</template>

<script setup lang="ts">
import { computed, inject, type Ref } from 'vue';
import { cn } from '../../lib/utils';

const props = defineProps<{
  value: string | number;
  class?: string;
}>();

const selected = inject<Ref<string | number | undefined>>('uiRadioValue');
const update = inject<(value: any) => void>('uiRadioUpdate', () => {});

const classes = computed(() => cn(
  'h-8 rounded-[10px] px-3 text-[13px] font-extrabold transition-colors',
  selected?.value === props.value ? 'bg-[#111827] text-white' : 'text-[#596273] hover:bg-white',
  props.class,
));
</script>
