import { computed, reactive, ref } from 'vue';
import {
  defineRolePolicyApi,
  getAllDefinedPermissionsApi,
  getAllRolesApi,
  getRolePermissionsApi,
  updateRolePermissionsApi,
  type PermissionGroupDto,
} from '../../api/identity';
import { useAuthStore } from '../../stores/auth';
import { Permissions } from '../../types/permissions';
import { notifyWarning } from '../../utils/feedback';
import {
  rolePermissionSummary,
  togglePermission,
  validateRoleName,
  type RoleCreateForm,
  type RolePermissionStatus,
} from './types';

export function useRoles() {
  const authStore = useAuthStore();
  const roles = ref<string[]>([]);
  const rolePermissions = ref<Record<string, string[]>>({});
  const rolePermissionStatus = ref<Record<string, RolePermissionStatus>>({});
  const loading = ref(false);
  const submitting = ref(false);
  const permissionGroups = ref<PermissionGroupDto[]>([]);
  const permGroupsLoading = ref(false);
  const showCreateModal = ref(false);
  const showPermEditor = ref(false);
  const createForm = reactive<RoleCreateForm>({ roleName: '', permissions: [] });
  const editRoleName = ref('');
  const editPermissions = ref<string[]>([]);
  const canSubmitPermissions = ref(false);

  const canDefineRole = computed(() =>
    authStore.hasPermission(Permissions.Role.Define),
  );
  const canUpdateRole = computed(() =>
    authStore.hasPermission(Permissions.Role.Update),
  );

  async function fetchPermGroups() {
    permGroupsLoading.value = true;
    try {
      permissionGroups.value = await getAllDefinedPermissionsApi();
    } catch {
      permissionGroups.value = [];
    } finally {
      permGroupsLoading.value = false;
    }
  }

  async function fetchRoles() {
    loading.value = true;
    try {
      const list = await getAllRolesApi();
      roles.value = list;
      const nextStatus: Record<string, RolePermissionStatus> = {};
      const nextPermissions: Record<string, string[]> = {};
      for (const role of list) {
        nextStatus[role] = 'loading';
        rolePermissionStatus.value = { ...nextStatus };
        try {
          const dto = await getRolePermissionsApi(role);
          nextPermissions[role] = dto.permissions;
          nextStatus[role] = 'loaded';
        } catch {
          nextStatus[role] = 'failed';
        }
        rolePermissions.value = { ...nextPermissions };
        rolePermissionStatus.value = { ...nextStatus };
      }
    } catch {
      roles.value = [];
      rolePermissions.value = {};
      rolePermissionStatus.value = {};
    } finally {
      loading.value = false;
    }
  }

  function summarizeRole(role: string) {
    return rolePermissionSummary(
      role,
      rolePermissions.value,
      rolePermissionStatus.value,
    );
  }

  async function openCreateModal() {
    createForm.roleName = '';
    createForm.permissions = [];
    showCreateModal.value = true;
    await fetchPermGroups();
  }

  function toggleCreatePerm(permission: string, checked: boolean) {
    togglePermission(createForm.permissions, permission, checked);
  }

  async function submitCreate() {
    const validationMessage = validateRoleName(createForm.roleName);
    if (validationMessage) {
      notifyWarning(validationMessage);
      return;
    }
    submitting.value = true;
    try {
      await defineRolePolicyApi({
        roleName: createForm.roleName,
        permissions: createForm.permissions,
      });
      showCreateModal.value = false;
      await fetchRoles();
    } catch {
      /* feedback handled by http client */
    } finally {
      submitting.value = false;
    }
  }

  async function openPermEditor(role: string) {
    if (rolePermissionStatus.value[role] !== 'loaded') {
      notifyWarning('角色权限尚未成功加载，请刷新后重试');
      return;
    }
    editRoleName.value = role;
    editPermissions.value = [...(rolePermissions.value[role] ?? [])];
    canSubmitPermissions.value = false;
    showPermEditor.value = true;
    await fetchPermGroups();
    canSubmitPermissions.value = permissionGroups.value.length > 0;
  }

  function toggleEditPerm(permission: string, checked: boolean) {
    togglePermission(editPermissions.value, permission, checked);
  }

  async function submitPermissions() {
    if (!canSubmitPermissions.value) {
      notifyWarning('权限清单加载失败，不能保存角色权限');
      return;
    }
    submitting.value = true;
    try {
      await updateRolePermissionsApi(editRoleName.value, editPermissions.value);
      rolePermissions.value[editRoleName.value] = [...editPermissions.value];
      rolePermissionStatus.value[editRoleName.value] = 'loaded';
      showPermEditor.value = false;
    } catch {
      /* feedback handled by http client */
    } finally {
      submitting.value = false;
    }
  }

  return {
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
  };
}
