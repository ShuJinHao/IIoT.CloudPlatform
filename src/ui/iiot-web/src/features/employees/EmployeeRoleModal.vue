<template>
  <UiModal
    :show="show"
    preset="card"
    title="维护员工角色"
    style="width: 600px;"
    :mask-closable="false"
    @update:show="requestClose"
  >
    <div class="employee-role-modal" :class="{ 'is-busy': loading || submitting }">
      <LoadingState v-if="loading" :rows="5" />
      <div v-else-if="ready && target && detail" class="form-stack">
        <div class="role-target-card">
          <div>
            <div class="role-target-card__name">{{ target.realName }}</div>
            <div class="role-target-card__meta">工号 {{ target.employeeNo }}</div>
          </div>
          <UiTag :type="target.isActive ? 'success' : 'default'" :bordered="false" size="small">
            {{ target.isActive ? '在职' : '停用' }}
          </UiTag>
        </div>

        <div class="form-field">
          <span class="form-label">当前角色</span>
          <div v-if="currentRoleNames.length" class="detail-chips">
            <UiTag
              v-for="roleName in currentRoleNames"
              :key="roleName.toLowerCase()"
              :type="missingRoleNames.includes(roleName) ? 'warning' : 'info'"
              :bordered="false"
              size="small"
            >
              {{ roleName }}
            </UiTag>
          </div>
          <span v-else class="detail-row__value detail-row__value--muted">未分配角色</span>
        </div>

        <p v-if="hasMultipleRoles" class="role-inline-notice role-inline-notice--warning">
          检测到多个遗留角色。请明确选择唯一角色，或选择“不分配角色”完成清理。
        </p>
        <p v-if="missingRoleNames.length" class="role-inline-notice role-inline-notice--warning">
          历史角色 {{ missingRoleNames.join('、') }} 已不在正式候选清单中，仅供核对，不能继续选择。
        </p>

        <div class="form-field">
          <label class="form-label" for="employee-role-selection">唯一系统角色</label>
          <UiSelect
            id="employee-role-selection"
            :value="selection"
            :options="roleOptions"
            placeholder="请选择唯一角色或清除角色"
            :disabled="loading || submitting || !canManageRole"
            @update:value="updateSelection"
          />
        </div>

        <p class="role-inline-notice role-inline-notice--session">
          保存后，目标员工现有 Access Token、Refresh Token 和 OIDC 会话立即失效，必须重新登录。
        </p>
      </div>
    </div>

    <template #footer>
      <div class="modal-actions">
        <UiButton :disabled="loading || submitting" @click="requestClose">取消</UiButton>
        <UiButton
          type="primary"
          :loading="submitting"
          :disabled="loading || submitting || !canSubmit"
          @click="$emit('submit')"
        >
          保存角色
        </UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiSelectOption } from '../../components/ui/types';
import type { EmployeeDetailDto, EmployeeListItemDto } from './api';

const props = defineProps<{
  show: boolean;
  target: EmployeeListItemDto | null;
  detail: EmployeeDetailDto | null;
  currentRoleNames: string[];
  missingRoleNames: string[];
  roleOptions: UiSelectOption[];
  selection: string;
  loading: boolean;
  ready: boolean;
  submitting: boolean;
  canManageRole: boolean;
  canSubmit: boolean;
  hasMultipleRoles: boolean;
}>();

const emit = defineEmits<{
  'request-close': [];
  'update-selection': [value: string];
  submit: [];
}>();

function requestClose() {
  if (!props.loading && !props.submitting) {
    emit('request-close');
  }
}

function updateSelection(value: string | number | boolean | null) {
  if (!props.loading && !props.submitting && props.canManageRole) {
    emit('update-selection', typeof value === 'string' ? value : '');
  }
}
</script>
