<template>
  <NiondDataPage class="employee-page" page-key="employees" title="员工花名册" subtitle="管理车间操作人员档案与设备双维管辖权">
    <template #actions>
      <UiButton type="primary" v-permission="'Employee.Onboard'" @click="openOnboardModal">
        <template #icon><Plus :size="14" /></template>
        员工入职
      </UiButton>
    </template>
    <template #toolbar>
      <NiondToolbar>
        <div class="filter-row">
          <UiInput v-model:value="keyword" placeholder="搜索工号或姓名..." clearable size="small" style="max-width: 320px;" @input="onSearchInput" @keyup.enter="fetchList" @clear="onClearKeyword">
            <template #prefix><Search :size="14" /></template>
          </UiInput>
          <UiTag round :bordered="false" size="small">共 {{ metaData.totalCount }} 人</UiTag>
        </div>
      </NiondToolbar>
    </template>
    <NiondTableCard class="employee-page__table-card">
      <UiDataTable class="employee-page__table" :columns="columns" :data="employees" :loading="loading" :bordered="false" :single-line="false" :row-key="rowKey" size="small">
        <template #empty><EmptyState title="未找到员工" description="没有任何人员档案或不满足当前的搜索条件。" /></template>
      </UiDataTable>
      <div v-if="metaData.totalPages > 1" class="pagination-wrap">
        <UiPagination :page="currentPage" :page-count="metaData.totalPages" :item-count="metaData.totalCount" :page-size="10" show-quick-jumper @update:page="onPageChange" />
      </div>
    </NiondTableCard>

    <EmployeeOnboardModal v-model:show="showOnboardModal" :form="onboardForm" :role-options="roleOptions" :can-update-access="canUpdateAccess" :submitting="submitting" @submit="submitOnboard" />
    <EmployeeEditModal v-model:show="showEditModal" :form="editForm" :target="editTarget" :role-options="roleOptions" :can-update-access="canUpdateAccess" :role-load-failed="editRoleLoadFailed" :submitting="submitting" @submit="submitEdit" />
    <EmployeeAccessModal v-model:show="showAccessModal" :form="accessForm" :devices="allDevices" :loading="accessLoading" :submitting="submitting" @toggle-device="toggleDeviceAccess" @submit="submitAccess" />
    <EmployeeDetailModal v-model:show="showDetailModal" :detail="detailData" :device-name-map="deviceNameMap" />
    <EmployeeResetPasswordModal v-model:show="showResetPwdModal" :target="resetPwdTarget" :form="resetPwdForm" :submitting="submitting" @submit="submitResetPwd" />
    <EmployeePersonalPermissionsModal v-model:show="showPersonalPermModal" :target="personalPermTarget" :loading="personalPermLoading" :permission-groups="permissionGroups" :selected-permissions="personalPermForm" :submitting="submitting" @toggle-permission="togglePersonalPerm" @submit="submitPersonalPerm" />
    <EmployeeConfirmModal v-model:show="confirmDialog.show" :dialog="confirmDialog" :submitting="submitting" />
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { Plus, Search } from 'lucide-vue-next';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { EmployeeListItemDto } from './api';
import { createEmployeeColumns } from './columns';
import EmployeeAccessModal from './EmployeeAccessModal.vue';
import EmployeeConfirmModal from './EmployeeConfirmModal.vue';
import EmployeeDetailModal from './EmployeeDetailModal.vue';
import EmployeeEditModal from './EmployeeEditModal.vue';
import EmployeeOnboardModal from './EmployeeOnboardModal.vue';
import EmployeePersonalPermissionsModal from './EmployeePersonalPermissionsModal.vue';
import EmployeeResetPasswordModal from './EmployeeResetPasswordModal.vue';
import { useEmployees } from './useEmployees';
import './employee-page.css';

const employeeState = useEmployees();
const columns = createEmployeeColumns({
  canUpdateEmployee: () => employeeState.canUpdateEmployee.value,
  canUpdateAccess: () => employeeState.canUpdateAccess.value,
  canDeactivateEmployee: () => employeeState.canDeactivateEmployee.value,
  canTerminateEmployee: () => employeeState.canTerminateEmployee.value,
  canManagePersonalPermissions: () => employeeState.canManagePersonalPermissions.value,
  onDetail: employeeState.openDetailModal,
  onEdit: employeeState.openEditModal,
  onResetPassword: employeeState.openResetPwdModal,
  onAccess: employeeState.openAccessModal,
  onPersonalPermissions: employeeState.openPersonalPermModal,
  onDeactivate: employeeState.handleDeactivate,
  onTerminate: employeeState.handleTerminate,
});
const rowKey = (row: EmployeeListItemDto) => row.id;

const {
  employees, loading, keyword, currentPage, metaData, submitting, canUpdateAccess, allDevices, deviceNameMap, roleOptions,
  showOnboardModal, onboardForm, showEditModal, editForm, editTarget, editRoleLoadFailed, showAccessModal, accessLoading,
  accessForm, showDetailModal, detailData, showResetPwdModal, resetPwdTarget, resetPwdForm, showPersonalPermModal,
  personalPermTarget, personalPermLoading, personalPermForm, permissionGroups, confirmDialog, initialize, fetchList,
  onSearchInput, onClearKeyword, onPageChange, openOnboardModal, submitOnboard, submitEdit, toggleDeviceAccess,
  submitAccess, submitResetPwd, togglePersonalPerm, submitPersonalPerm,
} = employeeState;

onMounted(initialize);
</script>
