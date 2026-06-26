<template>
  <NiondDataPage class="device-page" page-key="devices" title="设备台账" subtitle="管理云端设备档案、工序归属与客户端寻址 Code">
    <template #actions>
      <UiButton v-if="authStore.isAdmin" type="primary" @click="openRegisterModal">
        <template #icon><Plus :size="14" /></template>
        新建设备
      </UiButton>
    </template>

    <template #toolbar>
      <NiondToolbar>
        <div class="filter-row">
          <UiInput
            v-model:value="keyword"
            placeholder="搜索设备名称或 Code..."
            clearable
            size="small"
            style="max-width: 360px;"
            @input="onSearchInput"
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
          <EmptyState title="未找到设备" description="当前没有设备数据或未找到匹配的结果。" />
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
import { createDeviceColumns } from './columns';
import DeviceDeleteConfirm from './DeviceDeleteConfirm.vue';
import DeviceDetailDrawer from './DeviceDetailDrawer.vue';
import DeviceEditModal from './DeviceEditModal.vue';
import DeviceRegisterModal from './DeviceRegisterModal.vue';
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
  canUpdateDevice,
  canDeleteDevice,
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
  initialize,
  fetchList,
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
} = useDevices();

const columns = createDeviceColumns({
  canUpdateDevice: () => canUpdateDevice.value,
  canDeleteDevice: () => canDeleteDevice.value,
  processLabel,
  onDetail: openDetailPanel,
  onEdit: openEditModal,
  onDelete: handleDelete,
});
const rowKey = (row: DeviceListItemDto) => row.id;

onMounted(initialize);
</script>
