<template>
  <UiModal v-model:show="show" preset="card" title="员工入职建档" style="width: 640px;" :mask-closable="false">
    <div class="form-stack">
      <div class="form-section-label">基础信息</div>
      <div class="form-grid form-grid--2">
        <div class="form-field">
          <label class="form-label">工号 <span class="required">*</span></label>
          <UiInput v-model:value="form.EmployeeNo" placeholder="如：A10086" />
        </div>
        <div class="form-field">
          <label class="form-label">姓名 <span class="required">*</span></label>
          <UiInput v-model:value="form.RealName" placeholder="真实姓名" />
        </div>
        <div class="form-field">
          <label class="form-label">初始密码 <span class="required">*</span></label>
          <UiInput v-model:value="form.Password" type="password" show-password-on="click" placeholder="至少 8 位，含大小写和数字" />
        </div>
        <div v-if="canUpdateAccess" class="form-field">
          <label class="form-label">系统角色</label>
          <UiSelect v-model:value="form.RoleName" :options="roleOptions" placeholder="不分配角色" clearable />
        </div>
      </div>
      <div class="form-section-label">设备授权说明</div>
      <p class="form-hint">员工入职只创建账号、员工档案和初始角色。设备授权请在入职完成后，通过"管辖权配置"单独维护。</p>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">取消</UiButton>
        <UiButton type="primary" :loading="submitting" @click="$emit('submit')">确认入职</UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import type { UiSelectOption } from '../../components/ui/types';
import type { EmployeeOnboardForm } from './types';

defineEmits<{ submit: [] }>();
const show = defineModel<boolean>('show', { required: true });
defineProps<{
  form: EmployeeOnboardForm;
  roleOptions: UiSelectOption[];
  canUpdateAccess: boolean;
  submitting: boolean;
}>();
</script>
