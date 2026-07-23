<template>
  <UiModal v-model:show="show" preset="card" title="永久删除发布组件" style="width: 560px;" :mask-closable="false">
    <div v-if="target" class="hard-delete-modal">
      <p class="hard-delete-warning">
        将永久删除组件「{{ target.componentName }}」及其全部版本、历史记录和受控发布文件。此操作不可撤销。
      </p>
      <div class="hard-delete-field">
        <span class="hard-delete-label">
          确认内容（请输入 {{ target.kind === 'plugin' ? 'Module ID' : '宿主名称' }}：
          <code>{{ expectedConfirm }}</code>）
        </span>
        <UiInput v-model:value="confirmText" :placeholder="expectedConfirm" />
      </div>
      <div class="hard-delete-field">
        <span class="hard-delete-label">删除原因（必填）</span>
        <UiInput v-model:value="reason" type="textarea" placeholder="说明为什么永久删除该组件" />
      </div>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="onCancel">取消</UiButton>
        <UiButton type="error" :loading="submitting" @click="onSubmit">永久删除</UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import type { ReleaseCatalogRow } from './types';

const props = defineProps<{
  target: ReleaseCatalogRow | null;
  submitting: boolean;
}>();

const emit = defineEmits<{
  cancel: [];
  submit: [];
}>();

const show = defineModel<boolean>('show', { required: true });
const confirmText = defineModel<string>('confirmText', { required: true });
const reason = defineModel<string>('reason', { required: true });

const expectedConfirm = computed(() =>
  props.target ? (props.target.kind === 'plugin' ? props.target.componentCode : props.target.componentName) : '',
);

function onCancel() {
  emit('cancel');
}

function onSubmit() {
  emit('submit');
}
</script>
