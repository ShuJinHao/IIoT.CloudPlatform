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
      <div class="form-field">
        <label class="form-label">账号状态</label>
        <div class="toggle-row">
          <UiSwitch v-model:value="form.IsActive" />
          <span class="toggle-label">{{ form.IsActive ? '启用' : '停用' }}</span>
        </div>
      </div>
      <div v-if="canUpdateAccess" class="form-field">
        <label class="form-label">系统角色</label>
        <UiSelect v-model:value="form.RoleName" :options="roleOptions" placeholder="不分配角色" clearable :disabled="roleLoadFailed" />
        <p v-if="roleLoadFailed" class="form-error">角色信息加载失败，本次保存不会变更系统角色。</p>
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
import UiSelect from '../../components/ui/UiSelect.vue';
import UiSwitch from '../../components/ui/UiSwitch.vue';
import type { UiSelectOption } from '../../components/ui/types';
import type { EmployeeListItemDto } from './api';
import type { EmployeeEditForm } from './types';

defineEmits<{ submit: [] }>();
const show = defineModel<boolean>('show', { required: true });
defineProps<{
  form: EmployeeEditForm;
  target: EmployeeListItemDto | null;
  roleOptions: UiSelectOption[];
  canUpdateAccess: boolean;
  roleLoadFailed: boolean;
  submitting: boolean;
}>();
</script>
