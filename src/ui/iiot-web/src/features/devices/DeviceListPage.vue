<template>
  <NiondDataPage class="device-page" page-key="devices" title="设备台账" subtitle="管理云端设备档案、工序归属与客户端寻址 Code">
    <template #actions>
      <UiButton
        v-if="authStore.isAdmin"
        type="primary"
        :disabled="!selectedProcessId"
        @click="openRegisterModal"
      >
        <template #icon><Plus :size="14" /></template>
        新建设备
      </UiButton>
    </template>

    <template #toolbar>
      <NiondToolbar>
        <div class="filter-row">
          <UiSelect
            data-testid="device-ledger-process-select"
            :value="selectedProcessId"
            :options="processOptions"
            placeholder="请选择工序"
            :loading="processLoading"
            :disabled="processLoading || processOptions.length === 0"
            clearable
            style="min-width: 240px; max-width: 320px;"
            @update:value="selectProcess"
          />
          <UiInput
            v-model:value="keyword"
            placeholder="搜索设备名称或 Code..."
            clearable
            size="small"
            style="max-width: 360px;"
            @input="onSearchInput"
            :disabled="!selectedProcessId"
            @keyup.enter="fetchList"
            @clear="onClearKeyword"
          >
            <template #prefix><Search :size="14" /></template>
          </UiInput>
          <UiTag round :bordered="false" size="small">共 {{ metaData.totalCount }} 台</UiTag>
        </div>
      </NiondToolbar>
    </template>

    <NiondTableCard class="device-page__table-card">
      <UiDataTable
        class="device-page__table"
        :columns="columns"
        :data="devices"
        :loading="loading"
        :bordered="false"
        :single-line="false"
        :row-key="rowKey"
        size="small"
      >
        <template #empty>
          <EmptyState
            v-if="processError"
            title="设备工序加载失败"
            :description="processError"
          >
            <template #action>
              <UiButton size="small" type="primary" @click="fetchProcesses">重新加载</UiButton>
            </template>
          </EmptyState>
          <EmptyState
            v-else-if="!selectedProcessId"
            title="请先选择工序"
            description="设备台账不会自动选择工序；选定后才加载该工序的设备。"
          />
          <EmptyState
            v-else-if="listError"
            title="设备列表加载失败"
            :description="listError"
          >
            <template #action>
              <UiButton size="small" type="primary" @click="fetchList">重新加载</UiButton>
            </template>
          </EmptyState>
          <EmptyState
            v-else
            title="当前工序暂无设备"
            description="管理员可在当前工序上下文中创建首台设备。"
          />
        </template>
      </UiDataTable>
      <div v-if="metaData.totalPages > 1" class="pagination-wrap">
        <UiPagination
          :page="currentPage"
          :page-count="metaData.totalPages"
          :item-count="metaData.totalCount"
          :page-size="10"
          show-quick-jumper
          @update:page="onPageChange"
        />
      </div>
    </NiondTableCard>

    <DeviceRegisterModal v-model:show="showRegisterModal" :form="registerForm" :process-options="processOptions" :submitting="submitting" @submit="submitRegister" />
    <DeviceEditModal v-model:show="showEditModal" :form="editForm" :submitting="submitting" @submit="submitEdit" />
    <DeviceDetailDrawer v-model:show="showDetailPanel" :device="selectedDevice" :process-name-map="processNameMap" />
    <DeviceDeleteConfirm
      v-model:show="confirmDialog.show"
      :dialog="confirmDialog"
      :deletion-impact-rows="deletionImpactRows"
      :confirm-disabled="confirmDisabled"
      :submitting="submitting"
    />
    <DeviceProcessMigrationModal
      v-model:show="migrationDialog.show"
      :dialog="migrationDialog"
      :process-options="processOptions"
      :submitting="submitting"
      @target-change="selectMigrationTarget"
      @submit="submitMigration"
    />
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
import UiSelect from '../../components/ui/UiSelect.vue';
import UiTag from '../../components/ui/UiTag.vue';
import { createDeviceColumns } from './columns';
import DeviceDeleteConfirm from './DeviceDeleteConfirm.vue';
import DeviceDetailDrawer from './DeviceDetailDrawer.vue';
import DeviceEditModal from './DeviceEditModal.vue';
import DeviceRegisterModal from './DeviceRegisterModal.vue';
import DeviceProcessMigrationModal from './DeviceProcessMigrationModal.vue';
import { useDevices } from './useDevices';
import type { DeviceListItemDto } from './api';
import './device-page.css';

const {
  authStore,
  devices,
  loading,
  keyword,
  currentPage,
  metaData,
  submitting,
  selectedProcessId,
  processLoading,
  processError,
  listError,
  canUpdateDevice,
  canDeleteDevice,
  canMigrateDevice,
  processOptions,
  processNameMap,
  showRegisterModal,
  registerForm,
  showDetailPanel,
  selectedDevice,
  showEditModal,
  editForm,
  confirmDialog,
  deletionImpactRows,
  confirmDisabled,
  migrationDialog,
  initialize,
  fetchProcesses,
  fetchList,
  selectProcess,
  onSearchInput,
  onClearKeyword,
  onPageChange,
  processLabel,
  openRegisterModal,
  submitRegister,
  openDetailPanel,
  openEditModal,
  submitEdit,
  handleDelete,
  openMigrationDialog,
  selectMigrationTarget,
  submitMigration,
} = useDevices();

const columns = createDeviceColumns({
  canUpdateDevice: () => canUpdateDevice.value,
  canDeleteDevice: () => canDeleteDevice.value,
  canMigrateDevice: () => canMigrateDevice.value,
  processLabel,
  onDetail: openDetailPanel,
  onEdit: openEditModal,
  onDelete: handleDelete,
  onMigrate: openMigrationDialog,
});
const rowKey = (row: DeviceListItemDto) => row.id;

onMounted(initialize);
</script>
