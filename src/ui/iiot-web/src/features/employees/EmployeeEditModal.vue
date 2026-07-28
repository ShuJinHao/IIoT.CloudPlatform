<template>
  <UiModal v-model:show="show" preset="card" title="编辑员工档案" style="width: 480px;" :mask-closable="false">
    <div class="form-stack">
      <div class="form-field">
        <label class="form-label">工号</label>
        <UiInput :value="target?.employeeNo" disabled />
      </div>
      <div class="form-field">
        <label class="form-label">姓名 <span class="required">*</span></label>
        <UiInput v-model:value="form.RealName" />
      </div>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">取消</UiButton>
        <UiButton type="primary" :loading="submitting" @click="$emit('submit')">保存修改</UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import type { EmployeeListItemDto } from './api';
import type { EmployeeEditForm } from './types';

defineEmits<{ submit: [] }>();
const show = defineModel<boolean>('show', { required: true });
defineProps<{
  form: EmployeeEditForm;
  target: EmployeeListItemDto | null;
  submitting: boolean;
}>();
</script>
