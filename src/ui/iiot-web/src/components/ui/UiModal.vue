<template>
  <Teleport to="body">
    <div v-if="show" class="ui-modal" @mousedown.self="onMaskClick">
      <section class="ui-modal__panel" :style="style">
        <button class="ui-modal__close" type="button" aria-label="关闭" @click="close">✕</button>
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

function close() {
  emit('update:show', false);
}

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
  position: relative;
  display: flex;
  flex-direction: column;
  width: min(92vw, 560px);
  max-height: min(88vh, 920px);
  overflow: hidden;
  border-radius: 26px;
  background: var(--card);
  box-shadow: 0 24px 80px rgba(17, 24, 39, 0.22);
}

/* 常驻关闭按钮：任何弹窗都能一键退出，不依赖底部取消键或点遮罩 */
.ui-modal__close {
  position: absolute;
  top: 18px;
  right: 18px;
  z-index: 1;
  display: grid;
  place-items: center;
  width: 32px;
  height: 32px;
  border: none;
  border-radius: 10px;
  background: var(--bg-2);
  color: var(--text-1);
  font-size: 15px;
  line-height: 1;
  cursor: pointer;
  transition: background 0.15s ease, color 0.15s ease;
}

.ui-modal__close:hover {
  background: var(--bg-3);
  color: var(--text-0);
}

.ui-modal__header {
  flex-shrink: 0;
  padding: 26px 60px 0 28px;
}

.ui-modal__header h2 {
  margin: 0;
  color: #111827;
  font-size: 22px;
  font-weight: 900;
}

/* 内容区滚动，头部与底部始终可见 */
.ui-modal__body {
  flex: 1 1 auto;
  overflow: auto;
  padding: 24px 28px;
}

.ui-modal__footer {
  flex-shrink: 0;
  padding: 20px 28px 26px;
  border-top: 1px solid var(--border);
  background: var(--card);
}
</style>
