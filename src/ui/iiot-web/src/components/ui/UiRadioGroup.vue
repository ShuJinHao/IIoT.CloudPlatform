<template>
  <div class="ui-radio-group">
    <slot />
  </div>
</template>

<script setup lang="ts">
import { provide, toRef } from 'vue';

const props = defineProps<{
  value?: string | number;
}>();

const emit = defineEmits<{
  'update:value': [value: any];
}>();

provide('uiRadioValue', toRef(props, 'value'));
provide('uiRadioUpdate', (value: string | number) => emit('update:value', value));
</script>

<style scoped>
.ui-radio-group {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px;
  border-radius: 14px;
  background: #f4f7f8;
}
</style>
