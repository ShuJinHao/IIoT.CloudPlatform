<template>
  <NiondDataPage
    class="role-page"
    page-key="roles"
    title="角色与权限"
    subtitle="定义系统角色并配置行为权限点策略"
  >
    <template #actions>
      <UiButton v-if="canDefineRole" type="primary" @click="openCreateModal">
        <template #icon><Plus :size="14" /></template>
        定义新角色
      </UiButton>
    </template>

    <LoadingState v-if="loading" :rows="3" variant="card" />
    <CardSurface v-else-if="roles.length === 0">
      <EmptyState title="暂无角色数据" description="点击右上角「定义新角色」创建第一个角色。" />
    </CardSurface>
    <div v-else class="role-grid">
      <RoleCard
        v-for="role in roles"
        :key="role"
        :role="role"
        :permissions="rolePermissions[role] ?? []"
        :status="rolePermissionStatus[role]"
        :summary="summarizeRole(role)"
        :can-update="canUpdateRole"
        @edit="openPermEditor"
      />
    </div>

    <RoleCreateModal
      v-model:show="showCreateModal"
      :form="createForm"
      :permission-groups="permissionGroups"
      :loading-groups="permGroupsLoading"
      :submitting="submitting"
      @toggle="toggleCreatePerm"
      @submit="submitCreate"
    />
    <RolePermissionModal
      v-model:show="showPermEditor"
      :role-name="editRoleName"
      :permissions="editPermissions"
      :permission-groups="permissionGroups"
      :loading-groups="permGroupsLoading"
      :submitting="submitting"
      :can-submit="canSubmitPermissions"
      @toggle="toggleEditPerm"
      @submit="submitPermissions"
    />
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { Plus } from 'lucide-vue-next';
import CardSurface from '../../components/layout/CardSurface.vue';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import RoleCard from './RoleCard.vue';
import RoleCreateModal from './RoleCreateModal.vue';
import RolePermissionModal from './RolePermissionModal.vue';
import { useRoles } from './useRoles';
import './role-page.css';

const {
  roles,
  rolePermissions,
  rolePermissionStatus,
  loading,
  submitting,
  permissionGroups,
  permGroupsLoading,
  showCreateModal,
  createForm,
  showPermEditor,
  editRoleName,
  editPermissions,
  canSubmitPermissions,
  canDefineRole,
  canUpdateRole,
  fetchRoles,
  summarizeRole,
  openCreateModal,
  toggleCreatePerm,
  submitCreate,
  openPermEditor,
  toggleEditPerm,
  submitPermissions,
} = useRoles();

onMounted(fetchRoles);
</script>
