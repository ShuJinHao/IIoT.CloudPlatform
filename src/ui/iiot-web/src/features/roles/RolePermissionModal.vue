<template>
  <UiModal
    :show="show"
    preset="card"
    style="width: 720px;"
    :mask-closable="false"
    @update:show="$emit('update:show', $event)"
  >
    <template #header>
      <div class="modal-header-stack">
        <div class="modal-header-title">编辑角色权限</div>
        <div class="modal-header-sub">角色：{{ roleName }}</div>
      </div>
    </template>
    <LoadingState v-if="loadingGroups" :rows="6" />
    <PermissionSelector
      v-else
      :groups="permissionGroups"
      :selected="permissions"
      @toggle="(permission, checked) => $emit('toggle', permission, checked)"
    />
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="$emit('update:show', false)">取消</UiButton>
        <UiButton
          type="primary"
          :loading="submitting"
          :disabled="!canSubmit"
          @click="$emit('submit')"
        >
          保存权限
        </UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import type { PermissionGroupDto } from '../../api/identity';
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiModal from '../../components/ui/UiModal.vue';
import PermissionSelector from './PermissionSelector.vue';

defineProps<{
  show: boolean;
  roleName: string;
  permissions: string[];
  permissionGroups: PermissionGroupDto[];
  loadingGroups: boolean;
  submitting: boolean;
  canSubmit: boolean;
}>();

defineEmits<{
  'update:show': [show: boolean];
  toggle: [permission: string, checked: boolean];
  submit: [];
}>();
</script>
