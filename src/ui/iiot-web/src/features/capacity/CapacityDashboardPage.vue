<template>
  <NiondDataPage
    class="capacity-page"
    page-key="capacity"
    title="产能看板"
    subtitle="按工序定位授权设备；统计卡仅汇总当前设备与当前日期结果"
  >
    <ProductionContextToolbar
      :process-id="selectedProcessId"
      :device-id="selectedDeviceId"
      :process-options="processOptions"
      :device-options="deviceOptions"
      :context="context"
      :status="contextStatus"
      :has-authorized-devices="hasAuthorizedDevices"
      test-id-prefix="capacity"
      @update:process-id="selectProcess"
      @update:device-id="selectDevice"
    />

    <ProductionContextState
      v-if="contextState !== 'ready'"
      :state="contextState"
      :error="contextError"
      test-id-prefix="capacity"
      @retry="initialize"
    />

    <template v-else>
      <div class="capacity-page__stats">
        <StatCard label="当前设备完工弹夹数" :value="loading || listError ? '—' : formatInt(totalStats.total)" :unit="loading || listError ? '' : '个'" accent="brand" />
        <StatCard label="当前设备合格弹夹数" :value="loading || listError ? '—' : formatInt(totalStats.ok)" :unit="loading || listError ? '' : '个'" accent="success" />
        <StatCard label="当前设备不合格弹夹数" :value="loading || listError ? '—' : formatInt(totalStats.ng)" :unit="loading || listError ? '' : '个'" accent="error" />
        <StatCard
          label="当前设备综合良率"
          :value="loading || listError || totalStats.ratePercent === null ? '—' : totalStats.ratePercent.toFixed(1)"
          :unit="loading || listError || totalStats.ratePercent === null ? '' : '%'"
          :accent="loading || listError || totalStats.ratePercent === null ? 'info' : rateAccent(totalStats.ratePercent)"
        />
      </div>

      <NiondToolbar class="capacity-page__filter-card">
        <div class="capacity-page__filter-row">
          <div class="filter-field">
            <span class="filter-field__label">日期</span>
            <UiDatePicker
              v-model:formatted-value="dateFilter"
              value-format="yyyy-MM-dd"
              type="date"
              class="h-10"
              style="width: 180px;"
              @update:formatted-value="resetPageAndFetch"
            />
          </div>
          <UiButton quaternary size="small" @click="clearFilters">
            <template #icon><X :size="14" /></template>
            重置日期
          </UiButton>
        </div>
      </NiondToolbar>

      <NiondTableCard class="capacity-page__table-card">
        <UiDataTable
          class="capacity-page__table"
          :columns="columns"
          :data="records"
          :loading="loading"
          :row-key="rowKey"
        >
          <template #empty>
            <EmptyState
              v-if="listError"
              :title="listError.title"
              :description="listError.message"
            >
              <template #action>
                <UiButton size="small" type="primary" @click="fetchData">重新加载</UiButton>
              </template>
            </EmptyState>
            <EmptyState
              v-else
              title="当前设备暂无产能数据"
              description="所选设备在当前日期没有可展示的日汇总。"
            />
          </template>
        </UiDataTable>
        <div v-if="metaData.totalPages > 1" class="capacity-page__pagination">
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
    </template>
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { X } from 'lucide-vue-next';
import StatCard from '../../components/data/StatCard.vue';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiDatePicker from '../../components/ui/UiDatePicker.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import {
  ProductionContextState,
  ProductionContextToolbar,
} from '../../shared/production-context';
import { createCapacityDashboardColumns } from './columns';
import { useCapacityDashboard } from './useCapacityDashboard';
import { formatInt, rateAccent } from './types';
import './capacity-page.css';

const {
  records,
  loading,
  currentPage,
  metaData,
  dateFilter,
  processOptions,
  deviceOptions,
  selectedProcessId,
  selectedDeviceId,
  context,
  contextStatus,
  contextError,
  contextState,
  hasAuthorizedDevices,
  listError,
  totalStats,
  initialize,
  fetchData,
  selectProcess,
  selectDevice,
  resetPageAndFetch,
  clearFilters,
  onPageChange,
  goDetail,
  rowKey,
} = useCapacityDashboard();

const columns = createCapacityDashboardColumns({ onDetail: goDetail });

onMounted(initialize);
</script>
