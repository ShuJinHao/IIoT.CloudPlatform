<template>
  <NiondDataPage
    class="device-log-page"
    page-key="logs"
    title="设备日志"
    subtitle="按设备、级别、关键字和时间范围检索设备运行日志"
  >
    <NiondToolbar class="device-log-page__device-card">
      <div class="device-row">
        <span class="device-row__label">设备</span>
        <UiSelect
          v-model:value="selectedDeviceId"
          :options="deviceOptions"
          placeholder="请先选择设备"
          clearable
          size="small"
          style="width: 320px;"
          @update:value="onDeviceChange"
        />
      </div>
      <div v-if="deviceLoadError" class="device-row__error">
        {{ deviceLoadError }}
      </div>
    </NiondToolbar>

    <DeviceLogFilterCard
      v-if="selectedDeviceId"
      :current-mode="currentMode"
      :filters="filters"
      @switch-mode="switchMode"
      @search="doSearch"
    />

    <CardSurface v-if="!selectedDeviceId">
      <EmptyState
        title="请先选择一台设备"
        description="设备日志需要先指定查询目标，再选择查询模式与条件。"
      />
    </CardSurface>

    <NiondTableCard v-if="selectedDeviceId" class="device-log-page__table-card">
      <div v-if="!searched && !loading" class="hint-empty">
        <EmptyState title="设置条件后点击查询" description="未查询前不显示数据，避免误展示无关日志。" />
      </div>
      <UiDataTable
        v-else
        class="device-log-page__table"
        :columns="columns"
        :data="records"
        :loading="loading"
        :row-key="rowKey"
      >
        <template #empty>
          <EmptyState title="未找到日志" description="当前设备和查询条件下没有日志记录。" />
        </template>
      </UiDataTable>
      <div v-if="metaData.totalPages > 1" class="pagination-wrap">
        <UiPagination
          :page="currentPage"
          :page-count="metaData.totalPages"
          :item-count="metaData.totalCount"
          :page-size="20"
          show-quick-jumper
          @update:page="onPageChange"
        />
      </div>
    </NiondTableCard>
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import CardSurface from '../../components/layout/CardSurface.vue';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import { createDeviceLogColumns } from './columns';
import DeviceLogFilterCard from './DeviceLogFilterCard.vue';
import { useDeviceLogs } from './useDeviceLogs';
import './device-log-page.css';

const {
  currentMode,
  selectedDeviceId,
  loading,
  searched,
  currentPage,
  records,
  metaData,
  filters,
  deviceLoadError,
  deviceOptions,
  fetchDevices,
  switchMode,
  onDeviceChange,
  doSearch,
  onPageChange,
  rowKey,
} = useDeviceLogs();

const columns = createDeviceLogColumns();

onMounted(fetchDevices);
</script>
