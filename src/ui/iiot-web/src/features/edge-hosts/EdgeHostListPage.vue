<template>
  <NiondDataPage
    class="edge-host-page"
    page-key="edge-hosts"
    title="上位机 PLC 绑定"
    subtitle="维护云端上位机与 PLC 绑定配置，作为后续状态和产能串联的主配置入口"
  >
    <template #actions>
      <UiButton v-if="canManage" type="primary" @click="openCreateHostModal">
        <Plus :size="14" />
        新增上位机
      </UiButton>
    </template>

    <template #toolbar>
      <NiondToolbar>
        <div class="filter-row">
          <UiInput
            v-model:value="keyword"
            placeholder="搜索上位机、ClientCode、PLC 编码或名称..."
            clearable
            size="small"
            style="max-width: 420px;"
            @input="onSearchInput"
            @keyup.enter="fetchList"
            @clear="onClearKeyword"
          >
            <template #prefix><Search :size="14" /></template>
          </UiInput>
          <UiTag round :bordered="false" size="small">共 {{ metaData.totalCount }} 台上位机</UiTag>
        </div>
      </NiondToolbar>
    </template>

    <NiondTableCard>
      <UiDataTable
        class="edge-host-page__table"
        :columns="hostColumns"
        :data="hosts"
        :loading="loading"
        :row-key="rowKey"
      >
        <template #empty>
          <EmptyState title="未配置上位机" description="当前没有上位机 PLC 绑定配置。" />
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

    <EdgeHostFormModal
      v-model:show="showHostModal"
      :mode="hostFormMode"
      :form="hostForm"
      :device-options="deviceOptions"
      :client-code-preview="selectedDeviceCode"
      :submitting="submitting"
      @submit="submitHostForm"
    />
    <EdgeHostPlcDrawer
      v-model:show="showPlcDrawer"
      :host="selectedHost"
      :columns="plcColumns"
      :runtime-columns="runtimeColumns"
      :capacity-columns="capacityColumns"
      :runtime-states="runtimeStates"
      :capacity-summaries="capacitySummaries"
      :loading="detailLoading"
      :runtime-loading="runtimeLoading"
      :capacity-loading="capacityLoading"
      :capacity-date="capacityDate"
      :can-manage="canManage"
      :device-label="selectedHostDeviceLabel"
      @close="closePlcDrawer"
      @add="openCreatePlcModal"
    />
    <EdgeHostPlcFormModal
      v-model:show="showPlcFormModal"
      :mode="plcFormMode"
      :form="plcForm"
      :process-options="processOptions"
      :device-options="deviceOptions"
      :submitting="submitting"
      @submit="submitPlcForm"
    />
    <EdgeHostConfirmModal
      v-model:show="confirmDialog.show"
      :dialog="confirmDialog"
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
import EdgeHostConfirmModal from './EdgeHostConfirmModal.vue';
import EdgeHostFormModal from './EdgeHostFormModal.vue';
import EdgeHostPlcDrawer from './EdgeHostPlcDrawer.vue';
import EdgeHostPlcFormModal from './EdgeHostPlcFormModal.vue';
import { useEdgeHosts } from './useEdgeHosts';
import type { EdgeHostListItemDto } from './api';
import './edge-host-page.css';

const {
  hosts, loading, keyword, currentPage, metaData, submitting, detailLoading, runtimeLoading, capacityLoading,
  canManage, deviceOptions, processOptions, selectedDeviceCode, selectedHost, runtimeStates, capacitySummaries,
  capacityDate, selectedHostDeviceLabel,
  hostFormMode, plcFormMode, hostForm, plcForm, showHostModal, showPlcDrawer, showPlcFormModal,
  confirmDialog, hostColumns, plcColumns, runtimeColumns, capacityColumns, initialize, fetchList, onSearchInput,
  onClearKeyword, onPageChange, openCreateHostModal, submitHostForm, closePlcDrawer, openCreatePlcModal,
  submitPlcForm,
} = useEdgeHosts();

const rowKey = (row: EdgeHostListItemDto) => row.id;

onMounted(initialize);
</script>
