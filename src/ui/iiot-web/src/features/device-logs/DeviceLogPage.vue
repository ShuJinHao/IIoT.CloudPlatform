<template>
  <NiondDataPage
    class="device-log-page"
    page-key="logs"
    title="设备日志"
    subtitle="按工序定位授权设备，再按级别、关键字和时间范围检索运行日志"
  >
    <ProductionContextToolbar
      :process-id="selectedProcessId"
      :device-id="selectedDeviceId"
      :process-options="processOptions"
      :device-options="deviceOptions"
      :context="context"
      :status="contextStatus"
      :has-authorized-devices="hasAuthorizedDevices"
      test-id-prefix="device-logs"
      @update:process-id="selectProcess"
      @update:device-id="selectDevice"
    />

    <ProductionContextState
      v-if="contextState !== 'ready'"
      :state="contextState"
      :error="contextError"
      test-id-prefix="device-logs"
      @retry="initialize"
    />

    <template v-else>
      <DeviceLogFilterCard
        :current-mode="currentMode"
        :filters="filters"
        @switch-mode="switchMode"
        @search="doSearch"
      />

      <NiondTableCard class="device-log-page__table-card">
        <EmptyState v-if="queryError && !loading" title="设备日志加载失败" :description="queryError">
          <template #action><UiButton secondary size="small" @click="doSearch">重试</UiButton></template>
        </EmptyState>
        <div v-else-if="!searched && !loading" class="hint-empty">
          <EmptyState title="设置条件后点击查询" description="未查询前不显示数据，避免误展示无关日志。" />
        </div>
        <UiDataTable v-else class="device-log-page__table" :columns="columns" :data="records" :loading="loading" :row-key="rowKey">
          <template #empty>
            <EmptyState title="当前设备暂无日志数据" description="所选设备和查询条件下没有日志记录。" />
          </template>
        </UiDataTable>
        <div v-if="metaData.totalPages > 1" class="pagination-wrap">
          <UiPagination :page="currentPage" :page-count="metaData.totalPages" :item-count="metaData.totalCount" :page-size="20" show-quick-jumper @update:page="onPageChange" />
        </div>
      </NiondTableCard>
    </template>
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import { ProductionContextState, ProductionContextToolbar } from '../../shared/production-context';
import { createDeviceLogColumns } from './columns';
import DeviceLogFilterCard from './DeviceLogFilterCard.vue';
import { useDeviceLogs } from './useDeviceLogs';
import './device-log-page.css';

const {
  currentMode,
  loading,
  queryError,
  searched,
  currentPage,
  records,
  metaData,
  filters,
  processOptions,
  deviceOptions,
  selectedProcessId,
  selectedDeviceId,
  context,
  contextStatus,
  contextError,
  contextState,
  hasAuthorizedDevices,
  initialize,
  selectProcess,
  selectDevice,
  switchMode,
  doSearch,
  onPageChange,
  rowKey,
} = useDeviceLogs();

const columns = createDeviceLogColumns();

onMounted(initialize);
</script>
