<template>
  <UiModal v-model:show="show" preset="card" title="重置员工密码" style="width: 440px;" :mask-closable="false">
    <div class="form-stack">
      <div class="reset-target">
        <span class="reset-target__label">目标员工</span>
        <span class="reset-target__value">{{ target?.realName }}（{{ target?.employeeNo }}）</span>
      </div>
      <div class="form-field">
        <label class="form-label">新密码 <span class="required">*</span></label>
        <UiInput v-model:value="form.newPwd" type="password" show-password-on="click" placeholder="至少 8 位，含大小写和数字" />
      </div>
      <div class="form-field">
        <label class="form-label">确认新密码 <span class="required">*</span></label>
        <UiInput v-model:value="form.confirm" type="password" show-password-on="click" placeholder="再次输入新密码" />
      </div>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">取消</UiButton>
        <UiButton type="primary" :loading="submitting" @click="$emit('submit')">确认重置</UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import type { EmployeeListItemDto } from './api';
import type { EmployeeResetPasswordForm } from './types';

defineEmits<{ submit: [] }>();
const show = defineModel<boolean>('show', { required: true });
defineProps<{
  target: EmployeeListItemDto | null;
  form: EmployeeResetPasswordForm;
  submitting: boolean;
}>();
</script>
