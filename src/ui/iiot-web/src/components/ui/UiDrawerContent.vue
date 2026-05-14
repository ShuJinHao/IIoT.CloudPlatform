<template>
  <div class="ui-drawer-content">
    <header class="ui-drawer-content__header">
      <h2>{{ title }}</h2>
      <button v-if="closable" type="button" @click="close">关闭</button>
    </header>
    <div class="ui-drawer-content__body">
      <slot />
    </div>
  </div>
</template>

<script setup lang="ts">
import { inject } from 'vue';

withDefaults(defineProps<{
  title?: string;
  closable?: boolean;
}>(), {
  title: '',
  closable: false,
});

const close = inject<() => void>('uiDrawerClose', () => {});
</script>

<style scoped>
.ui-drawer-content {
  min-height: 100%;
}

.ui-drawer-content__header {
  position: sticky;
  top: 0;
  z-index: 2;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 28px 30px 18px;
  background: var(--card);
  border-bottom: 1px solid var(--border);
}

.ui-drawer-content__header h2 {
  margin: 0;
  color: #111827;
  font-size: 22px;
  font-weight: 900;
}

.ui-drawer-content__header button {
  height: 36px;
  border: 0;
  border-radius: 12px;
  padding: 0 14px;
  background: #f4f7f8;
  color: #596273;
  font-weight: 800;
}

.ui-drawer-content__body {
  padding: 24px 30px 32px;
}
</style>
