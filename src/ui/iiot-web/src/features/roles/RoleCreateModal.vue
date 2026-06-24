<template>
  <UiModal
    :show="show"
    preset="card"
    title="定义新角色"
    style="width: 720px;"
    :mask-closable="false"
    @update:show="$emit('update:show', $event)"
  >
    <div class="form-stack">
      <div class="form-field">
        <label class="form-label">角色名称 <span class="required">*</span></label>
        <UiInput v-model:value="form.roleName" placeholder="如：Operator、Supervisor" />
        <p class="form-hint">角色名建议使用英文，创建后不可修改</p>
      </div>
      <div class="form-field">
        <label class="form-label">分配权限点</label>
        <LoadingState v-if="loadingGroups" :rows="4" />
        <PermissionSelector
          v-else
          :groups="permissionGroups"
          :selected="form.permissions"
          @toggle="(permission, checked) => $emit('toggle', permission, checked)"
        />
      </div>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="$emit('update:show', false)">取消</UiButton>
        <UiButton type="primary" :loading="submitting" @click="$emit('submit')">
          确认创建
        </UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import type { PermissionGroupDto } from '../../api/identity';
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import PermissionSelector from './PermissionSelector.vue';
import type { RoleCreateForm } from './types';

defineProps<{
  show: boolean;
  form: RoleCreateForm;
  permissionGroups: PermissionGroupDto[];
  loadingGroups: boolean;
  submitting: boolean;
}>();

defineEmits<{
  'update:show': [show: boolean];
  toggle: [permission: string, checked: boolean];
  submit: [];
}>();
</script>
