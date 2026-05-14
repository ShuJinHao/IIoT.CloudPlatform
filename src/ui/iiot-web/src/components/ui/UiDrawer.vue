<template>
  <Teleport to="body">
    <div v-if="show" class="ui-drawer" @mousedown.self="emit('update:show', false)">
      <aside class="ui-drawer__panel" :style="{ width: panelWidth }">
        <slot />
      </aside>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { computed, provide } from 'vue';

const props = withDefaults(defineProps<{
  show?: boolean;
  width?: number | string;
  placement?: 'right' | 'left';
}>(), {
  width: 520,
  placement: 'right',
});

const emit = defineEmits<{
  'update:show': [value: boolean];
}>();

const panelWidth = computed(() => (typeof props.width === 'number' ? `${props.width}px` : props.width));

provide('uiDrawerClose', () => emit('update:show', false));
</script>

<style scoped>
.ui-drawer {
  position: fixed;
  inset: 0;
  z-index: 1000;
  display: flex;
  justify-content: flex-end;
  background: rgba(17, 24, 39, 0.32);
  backdrop-filter: blur(5px);
}

.ui-drawer__panel {
  height: 100%;
  max-width: 92vw;
  overflow: auto;
  border-radius: 28px 0 0 28px;
  background: var(--card);
  box-shadow: -24px 0 80px rgba(17, 24, 39, 0.22);
}
</style>
