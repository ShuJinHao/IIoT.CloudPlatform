<template>
  <NiondDataPage
    class="overview-page"
    page-key="device-client-overviews"
    title="设备运行与版本"
    subtitle="统一查看授权设备的客户端软件状态、当前版本与异常摘要"
  >
    <template #toolbar>
      <NiondToolbar>
        <div class="filter-row">
          <UiInput
            v-model:value="keyword"
            placeholder="搜索设备名称..."
            clearable
            size="small"
            style="max-width: 420px;"
            @input="onSearchInput"
            @keyup.enter="refresh"
            @clear="onClearKeyword"
          >
            <template #prefix><Search :size="14" /></template>
          </UiInput>
          <UiTag round :bordered="false" size="small">共 {{ total }} 台设备</UiTag>
        </div>
      </NiondToolbar>
    </template>

    <NiondTableCard>
      <div class="overview-body">
        <div v-if="loading" class="overview-state">加载中…</div>
        <div v-else-if="error" class="overview-state overview-state--error" role="alert">
          <EmptyState title="设备运行与版本加载失败" :description="error.message || '请稍后重试。'" />
          <UiButton size="small" secondary @click="refresh">重试</UiButton>
        </div>
        <template v-else-if="items.length > 0">
          <div class="sort-bar">
            <span class="sort-bar__label">排序</span>
            <button
              v-for="column in sortableColumns"
              :key="column.key"
              type="button"
              class="sort-button"
              :class="{ 'sort-button--active': sortBy === column.key }"
              @click="toggleSort(column.key)"
            >
              {{ column.label }}
              <span v-if="sortBy === column.key" class="sort-button__arrow">
                {{ sortDirection === 'asc' ? '↑' : '↓' }}
              </span>
            </button>
          </div>
          <UiDataTable
            class="overview-page__table"
            :columns="overviewColumns"
            :data="items"
            :row-key="rowKey"
          />
          <UiPagination
            class="overview-pagination"
            :page="page"
            :page-count="totalPages"
            :item-count="total"
            :page-size="pageSize"
            show-quick-jumper
            @update:page="gotoPage"
          />
        </template>
        <EmptyState v-else title="暂无可访问设备" description="当前账号权限范围内没有设备；已授权设备即使尚未上报运行心跳也会在此列出。" />
      </div>
    </NiondTableCard>

    <DeviceClientDetailDrawer
      v-model:show="showDetailDrawer"
      :device="selectedDevice"
      :can-view-plc="canViewPlcDetails"
      :can-view-release="canViewReleaseDetails"
      :plc-states="plcStates"
      :plc-loading="plcLoading"
      :plc-error="plcError"
      :release="releaseDetails"
      :release-loading="releaseLoading"
      :release-error="releaseError"
      @close="closeDetailDrawer"
      @retry-plc="retryPlcStates"
      @retry-release="retryReleaseDetails"
    />
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { Search } from 'lucide-vue-next';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import UiTag from '../../components/ui/UiTag.vue';
import DeviceClientDetailDrawer from './DeviceClientDetailDrawer.vue';
import { createOverviewColumns } from './columns';
import { useDeviceClientOverviews } from './useDeviceClientOverviews';
import { SORTABLE_COLUMNS } from './types';
import type { DeviceClientOverviewItemDto } from './api';
import './overview-page.css';

const {
  items, total, page, pageSize, totalPages, loading, error,
  keyword, sortBy, sortDirection,
  canViewPlcDetails, canViewReleaseDetails,
  showDetailDrawer, selectedDevice,
  plcStates, plcLoading, plcError,
  releaseDetails, releaseLoading, releaseError,
  refresh, onSearchInput, onClearKeyword, toggleSort, gotoPage,
  openDetailDrawer, retryPlcStates, retryReleaseDetails, closeDetailDrawer,
} = useDeviceClientOverviews();

const overviewColumns = createOverviewColumns({ onOpenDetail: openDetailDrawer });
const sortableColumns = SORTABLE_COLUMNS;
const rowKey = (row: DeviceClientOverviewItemDto) => row.deviceId;

onMounted(refresh);
</script>
