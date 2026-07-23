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
        <UiInput
          v-model:value="confirmText"
          :placeholder="expectedConfirm"
          @update:value="clearFeedback"
        />
        <p v-if="confirmError" class="hard-delete-error">{{ confirmError }}</p>
      </div>
      <div class="hard-delete-field">
        <span class="hard-delete-label">删除原因（必填）</span>
        <UiInput
          v-model:value="reason"
          type="textarea"
          placeholder="说明为什么永久删除该组件"
          @update:value="clearFeedback"
        />
        <p v-if="reasonError" class="hard-delete-error">{{ reasonError }}</p>
      </div>

      <div v-if="problem" class="hard-delete-problem" role="alert">
        <strong class="hard-delete-problem__title">{{ problem.title }}</strong>
        <p v-if="problem.detail" class="hard-delete-problem__detail">{{ problem.detail }}</p>
        <ul v-if="problem.errors.length > 0" class="hard-delete-problem__errors">
          <li v-for="(item, index) in problem.errors" :key="index">{{ item }}</li>
        </ul>
        <p v-if="problem.deletionId" class="hard-delete-problem__deletion">
          删除操作 ID：<code>{{ problem.deletionId }}</code>
          <span class="hard-delete-problem__hint">可在「删除恢复」列表中按此 ID 重试。</span>
        </p>
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
import { computed, ref, watch } from 'vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import type { ReleaseCatalogRow } from './types';

export interface HardDeleteProblem {
  title: string;
  detail?: string;
  errors: string[];
  deletionId?: string;
}

const props = defineProps<{
  target: ReleaseCatalogRow | null;
  submitting: boolean;
  problem?: HardDeleteProblem | null;
}>();

const emit = defineEmits<{
  cancel: [];
  submit: [];
}>();

const show = defineModel<boolean>('show', { required: true });
const confirmText = defineModel<string>('confirmText', { required: true });
const reason = defineModel<string>('reason', { required: true });

const confirmError = ref('');
const reasonError = ref('');

const expectedConfirm = computed(() =>
  props.target ? (props.target.kind === 'plugin' ? props.target.componentCode : props.target.componentName) : '',
);

watch(show, (visible) => {
  if (visible) {
    confirmError.value = '';
    reasonError.value = '';
  }
});

function clearFeedback() {
  confirmError.value = '';
  reasonError.value = '';
}

function onCancel() {
  emit('cancel');
}

function onSubmit() {
  confirmError.value = '';
  reasonError.value = '';

  // 确认内容和原因都 trim 后再比较：trim 是提交语义，不回写输入框干扰正在输入的用户。
  const confirmValue = confirmText.value.trim();
  const reasonValue = reason.value.trim();
  let valid = true;

  if (confirmValue !== expectedConfirm.value) {
    confirmError.value = `确认内容不正确，请输入：${expectedConfirm.value}`;
    valid = false;
  }
  if (!reasonValue) {
    reasonError.value = '请填写非空删除原因。';
    valid = false;
  }

  // 无效输入不发请求，由 modal 内联提示；有效时把 trim 后的确认内容回写，保持单数据源一致。
  if (!valid) return;
  if (confirmValue !== confirmText.value) {
    confirmText.value = confirmValue;
  }
  emit('submit');
}
</script>
