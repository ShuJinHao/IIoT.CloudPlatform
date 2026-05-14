<template>
  <Teleport to="body">
    <div v-if="show" class="ui-modal" @mousedown.self="onMaskClick">
      <section class="ui-modal__panel" :style="style">
        <header v-if="title" class="ui-modal__header">
          <h2>{{ title }}</h2>
        </header>
        <div class="ui-modal__body">
          <slot />
        </div>
        <footer v-if="$slots.footer" class="ui-modal__footer">
          <slot name="footer" />
        </footer>
      </section>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
const props = withDefaults(defineProps<{
  show?: boolean;
  maskClosable?: boolean;
  preset?: string;
  title?: string;
  style?: Record<string, string> | string;
}>(), {
  maskClosable: true,
});

const emit = defineEmits<{
  'update:show': [value: boolean];
}>();

function onMaskClick() {
  if (props.maskClosable) emit('update:show', false);
}
</script>

<style scoped>
.ui-modal {
  position: fixed;
  inset: 0;
  z-index: 1000;
  display: grid;
  place-items: center;
  padding: 28px;
  background: rgba(17, 24, 39, 0.38);
  backdrop-filter: blur(6px);
}

.ui-modal__panel {
  width: min(92vw, 560px);
  max-height: min(88vh, 920px);
  overflow: auto;
  border-radius: 26px;
  background: var(--card);
  box-shadow: 0 24px 80px rgba(17, 24, 39, 0.22);
}

.ui-modal__header {
  padding: 26px 28px 0;
}

.ui-modal__header h2 {
  margin: 0;
  color: #111827;
  font-size: 22px;
  font-weight: 900;
}

.ui-modal__body {
  padding: 24px 28px;
}

.ui-modal__footer {
  padding: 0 28px 26px;
}
</style>
