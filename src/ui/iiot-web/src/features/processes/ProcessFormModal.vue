<template>
  <UiModal
    :show="show"
    preset="card"
    :title="editTarget ? '编辑工序' : '新建制造工序'"
    style="width: 480px;"
    :mask-closable="false"
    @update:show="$emit('update:show', $event)"
  >
    <div class="form-stack">
      <div class="form-field">
        <label class="form-label">工序编码 <span class="required">*</span></label>
        <UiInput
          v-model:value="form.processCode"
          placeholder="如：Stacking、Injection"
          class="mono-input"
        />
        <p v-if="!editTarget" class="form-hint">编码全局唯一，建议使用英文标识符</p>
      </div>
      <div class="form-field">
        <label class="form-label">工序名称 <span class="required">*</span></label>
        <UiInput v-model:value="form.processName" placeholder="如：叠片工序、注液工序" />
      </div>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="$emit('update:show', false)">取消</UiButton>
        <UiButton type="primary" :loading="submitting" @click="$emit('submit')">
          {{ editTarget ? '保存修改' : '确认创建' }}
        </UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import type { ProcessListItemDto } from './api';
import type { ProcessFormData } from './types';

defineProps<{
  show: boolean;
  form: ProcessFormData;
  editTarget: ProcessListItemDto | null;
  submitting: boolean;
}>();

defineEmits<{
  'update:show': [show: boolean];
  submit: [];
}>();
</script>
