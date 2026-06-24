<template>
  <UiModal v-model:show="show" preset="card" style="width: 720px;" :mask-closable="false">
    <template #header>
      <div class="modal-header-stack">
        <div class="modal-header-title">个人特批权限</div>
        <div class="modal-header-sub">{{ target?.realName }}（{{ target?.employeeNo }}）· 与角色权限是并集关系</div>
      </div>
    </template>
    <LoadingState v-if="loading" :rows="6" />
    <div v-else class="perm-selector">
      <div v-for="group in permissionGroups" :key="group.groupName" class="perm-group">
        <div class="perm-group__title">{{ group.groupName }}</div>
        <div class="perm-group__items">
          <UiCheckbox
            v-for="permission in group.permissions"
            :key="permission"
            :checked="selectedPermissions.includes(permission)"
            @update:checked="(checked: boolean) => $emit('toggle-permission', permission, checked)"
          >
            {{ permission }}
          </UiCheckbox>
        </div>
      </div>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">取消</UiButton>
        <UiButton type="primary" :loading="submitting" @click="$emit('submit')">保存特批权限</UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiCheckbox from '../../components/ui/UiCheckbox.vue';
import UiModal from '../../components/ui/UiModal.vue';
import type { PermissionGroupDto } from '../../api/identity';
import type { EmployeeListItemDto } from './api';

defineEmits<{
  submit: [];
  'toggle-permission': [permission: string, checked: boolean];
}>();
const show = defineModel<boolean>('show', { required: true });
defineProps<{
  target: EmployeeListItemDto | null;
  loading: boolean;
  permissionGroups: PermissionGroupDto[];
  selectedPermissions: string[];
  submitting: boolean;
}>();
</script>
