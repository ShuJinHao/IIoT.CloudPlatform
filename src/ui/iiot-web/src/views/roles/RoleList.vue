<template>
  <NiondDataPage
    class="role-page"
    page-key="roles"
    title="角色与权限"
      subtitle="定义系统角色并配置行为权限点策略"
  >
      <template #actions>
        <UiButton
          type="primary"
          v-permission="'Role.Define'"
          @click="openCreateModal"
        >
          <template #icon>
            <svg viewBox="0 0 16 16" fill="none">
              <path d="M8 2v12M2 8h12" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>
            </svg>
          </template>
          定义新角色
        </UiButton>
      </template>

    <LoadingState v-if="loading" :rows="3" variant="card" />

    <CardSurface v-else-if="roles.length === 0">
      <EmptyState
        title="暂无角色数据"
        description="点击右上角「定义新角色」创建第一个角色。"
      />
    </CardSurface>

    <div v-else class="role-grid">
      <CardSurface
        v-for="role in roles"
        :key="role"
        class="role-card"
        :class="{ 'role-card--admin': role === 'Admin' }"
        hoverable
      >
        <div class="role-card__head">
          <div class="role-card__name-block">
            <h3 class="role-card__name">{{ role }}</h3>
            <UiTag
              v-if="role === 'Admin'"
              size="small"
              :bordered="false"
              type="warning"
            >
              系统内置
            </UiTag>
          </div>
          <UiButton
            v-if="role !== 'Admin'"
            v-permission="'Role.Update'"
            size="tiny"
            secondary
            @click="openPermEditor(role)"
          >
            编辑权限
          </UiButton>
        </div>

        <div class="role-card__body">
          <div class="role-card__meta">
            <span class="role-card__meta-label">权限点</span>
            <span class="role-card__meta-value">
              {{ rolePermissionSummary(role) }}
            </span>
          </div>
          <div v-if="rolePermissionStatus[role] === 'loaded'" class="role-card__chips">
            <UiTag
              v-for="perm in rolePermissions[role] ?? []"
              :key="perm"
              size="small"
              :bordered="false"
              type="info"
            >
              {{ perm }}
            </UiTag>
            <span
              v-if="(rolePermissions[role] ?? []).length === 0"
              class="role-card__no-perm"
            >
              暂无权限点
            </span>
          </div>
          <span v-else-if="rolePermissionStatus[role] === 'failed'" class="role-card__error">
            权限加载失败，请刷新后重试。
          </span>
          <span v-else class="role-card__loading">加载权限中...</span>
        </div>
      </CardSurface>
    </div>

    <!-- 创建角色 modal -->
    <UiModal
      v-model:show="showCreateModal"
      preset="card"
      title="定义新角色"
      style="width: 720px;"
      :mask-closable="false"
    >
      <div class="form-stack">
        <div class="form-field">
          <label class="form-label">角色名称 <span class="required">*</span></label>
          <UiInput
            v-model:value="createForm.roleName"
            placeholder="如：Operator、Supervisor"
          />
          <p class="form-hint">角色名建议使用英文，创建后不可修改</p>
        </div>
        <div class="form-field">
          <label class="form-label">分配权限点</label>
          <LoadingState v-if="permGroupsLoading" :rows="4" />
          <div v-else class="perm-selector">
            <div
              v-for="group in permissionGroups"
              :key="group.groupName"
              class="perm-group"
            >
              <div class="perm-group__title">{{ group.groupName }}</div>
              <div class="perm-group__items">
                <UiCheckbox
                  v-for="perm in group.permissions"
                  :key="perm"
                  :checked="createForm.permissions.includes(perm)"
                  @update:checked="(checked: boolean) => toggleCreatePerm(perm, checked)"
                >
                  {{ perm }}
                </UiCheckbox>
              </div>
            </div>
          </div>
        </div>
      </div>
      <template #footer>
        <div class="modal-actions">
          <UiButton @click="showCreateModal = false">取消</UiButton>
          <UiButton
            type="primary"
            :loading="submitting"
            @click="submitCreate"
          >
            确认创建
          </UiButton>
        </div>
      </template>
    </UiModal>

    <!-- 编辑权限 modal -->
    <UiModal
      v-model:show="showPermEditor"
      preset="card"
      style="width: 720px;"
      :mask-closable="false"
    >
      <template #header>
        <div class="modal-header-stack">
          <div class="modal-header-title">编辑角色权限</div>
          <div class="modal-header-sub">角色：{{ editRoleName }}</div>
        </div>
      </template>
      <LoadingState v-if="permGroupsLoading" :rows="6" />
      <div v-else class="perm-selector">
        <div
          v-for="group in permissionGroups"
          :key="group.groupName"
          class="perm-group"
        >
          <div class="perm-group__title">{{ group.groupName }}</div>
          <div class="perm-group__items">
            <UiCheckbox
              v-for="perm in group.permissions"
              :key="perm"
              :checked="editPermissions.includes(perm)"
              @update:checked="(checked: boolean) => toggleEditPerm(perm, checked)"
            >
              {{ perm }}
            </UiCheckbox>
          </div>
        </div>
      </div>
      <template #footer>
        <div class="modal-actions">
          <UiButton @click="showPermEditor = false">取消</UiButton>
          <UiButton
            type="primary"
            :loading="submitting"
            :disabled="!canSubmitPermissions"
            @click="submitPermissions"
          >
            保存权限
          </UiButton>
        </div>
      </template>
    </UiModal>
  </NiondDataPage>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue';
import {
  getAllRolesApi,
  defineRolePolicyApi,
  getRolePermissionsApi,
  updateRolePermissionsApi,
  getAllDefinedPermissionsApi,
  type PermissionGroupDto,
} from '../../api/identity';
import CardSurface from '../../components/layout/CardSurface.vue';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiCheckbox from '../../components/ui/UiCheckbox.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiTag from '../../components/ui/UiTag.vue';
import { notifyWarning } from '../../utils/feedback';

type RolePermissionStatus = 'loading' | 'loaded' | 'failed';

const roles = ref<string[]>([]);
const rolePermissions = ref<Record<string, string[]>>({});
const rolePermissionStatus = ref<Record<string, RolePermissionStatus>>({});
const loading = ref(false);
const submitting = ref(false);

const permissionGroups = ref<PermissionGroupDto[]>([]);
const permGroupsLoading = ref(false);

const fetchPermGroups = async () => {
  permGroupsLoading.value = true;
  try {
    permissionGroups.value =
      (await getAllDefinedPermissionsApi()) as unknown as PermissionGroupDto[];
  } catch {
    permissionGroups.value = [];
  } finally {
    permGroupsLoading.value = false;
  }
};

const fetchRoles = async () => {
  loading.value = true;
  try {
    const list = (await getAllRolesApi()) as unknown as string[];
    roles.value = list;
    const nextStatus: Record<string, RolePermissionStatus> = {};
    const nextPermissions: Record<string, string[]> = {};
    for (const role of list) {
      nextStatus[role] = 'loading';
      rolePermissionStatus.value = { ...nextStatus };
      try {
        const dto = (await getRolePermissionsApi(role)) as unknown as {
          roleName: string;
          permissions: string[];
        };
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
};

const rolePermissionSummary = (role: string) => {
  const status = rolePermissionStatus.value[role];
  if (status === 'loaded') return `${rolePermissions.value[role]?.length ?? 0} 项`;
  if (status === 'failed') return '加载失败';
  return '加载中';
};

// === 创建角色 ===
const showCreateModal = ref(false);
const createForm = reactive({
  roleName: '',
  permissions: [] as string[],
});

const openCreateModal = async () => {
  createForm.roleName = '';
  createForm.permissions = [];
  showCreateModal.value = true;
  await fetchPermGroups();
};

const toggleCreatePerm = (perm: string, checked: boolean) => {
  if (checked) {
    if (!createForm.permissions.includes(perm)) {
      createForm.permissions.push(perm);
    }
  } else {
    const idx = createForm.permissions.indexOf(perm);
    if (idx > -1) createForm.permissions.splice(idx, 1);
  }
};

const submitCreate = async () => {
  if (!createForm.roleName.trim()) {
    notifyWarning('角色名称不能为空');
    return;
  }
  submitting.value = true;
  try {
    await defineRolePolicyApi({
      roleName: createForm.roleName,
      permissions: createForm.permissions,
    });
    showCreateModal.value = false;
    fetchRoles();
  } catch {
    /* */
  } finally {
    submitting.value = false;
  }
};

// === 编辑权限 ===
const showPermEditor = ref(false);
const editRoleName = ref('');
const editPermissions = ref<string[]>([]);
const canSubmitPermissions = ref(false);

const openPermEditor = async (role: string) => {
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
};

const toggleEditPerm = (perm: string, checked: boolean) => {
  if (checked) {
    if (!editPermissions.value.includes(perm)) {
      editPermissions.value.push(perm);
    }
  } else {
    const idx = editPermissions.value.indexOf(perm);
    if (idx > -1) editPermissions.value.splice(idx, 1);
  }
};

const submitPermissions = async () => {
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
    /* */
  } finally {
    submitting.value = false;
  }
};

onMounted(() => fetchRoles());
</script>

<style scoped>
.role-page {
  font-family: var(--font-sans);
  color: var(--text-0);
}

.role-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
  gap: var(--space-4);
}

.role-card {
  transition: border-color var(--motion-base);
}
.role-card--admin {
  border-color: rgba(217, 119, 6, 0.28);
}

.role-card__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-3);
  margin-bottom: var(--space-4);
}
.role-card__name-block {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
}
.role-card__name {
  font-size: var(--fs-lg);
  font-weight: var(--fw-bold);
  color: var(--text-0);
  margin: 0;
  letter-spacing: 0;
}

.role-card__body {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}
.role-card__meta {
  display: flex;
  align-items: baseline;
  gap: var(--space-2);
}
.role-card__meta-label {
  font-size: var(--fs-xs);
  color: var(--text-2);
  text-transform: uppercase;
  letter-spacing: 0;
}
.role-card__meta-value {
  font-family: var(--font-mono);
  font-size: var(--fs-sm);
  color: var(--brand);
  font-weight: var(--fw-semibold);
}
.role-card__chips {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-1);
}
.role-card__no-perm {
  font-size: var(--fs-sm);
  color: var(--text-2);
}
.role-card__loading {
  font-size: var(--fs-sm);
  color: var(--text-2);
}
.role-card__error {
  font-size: var(--fs-sm);
  font-weight: var(--fw-semibold);
  color: var(--error);
}

/* 表单 */
.form-stack {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.form-field {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.form-label {
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
  color: var(--text-1);
}
.form-hint {
  font-size: var(--fs-xs);
  color: var(--text-2);
  margin: 0;
}
.required {
  color: var(--error);
}
.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
}
.modal-header-stack {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}
.modal-header-title {
  font-size: var(--fs-lg);
  font-weight: var(--fw-semibold);
  color: var(--text-0);
}
.modal-header-sub {
  font-size: var(--fs-sm);
  color: var(--text-1);
}

/* 权限选择器 */
.perm-selector {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
  max-height: 400px;
  overflow-y: auto;
}
.perm-group__title {
  font-size: var(--fs-xs);
  font-weight: var(--fw-semibold);
  color: var(--text-2);
  text-transform: uppercase;
  letter-spacing: 0;
  margin-bottom: var(--space-2);
  padding-bottom: var(--space-2);
  border-bottom: 1px solid var(--border);
}
.perm-group__items {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: var(--space-2);
}
</style>
