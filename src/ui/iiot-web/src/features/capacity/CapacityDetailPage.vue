<template>
  <NiondDataPage
    class="capacity-detail-page"
    :title="deviceName"
    :subtitle="subtitleText"
  >
    <template #actions>
      <UiButton size="small" :disabled="!canExport" @click="exportRows">
        <Download :size="14" />
        导出当前明细
      </UiButton>
      <UiButton quaternary size="small" @click="router.back()">
        <ChevronLeft :size="14" />
        返回
      </UiButton>
    </template>

    <ProductionContextToolbar
      :process-id="selectedProcessId"
      :device-id="selectedDeviceId"
      :process-options="processOptions"
      :device-options="deviceOptions"
      :context="context"
      :status="contextStatus"
      :has-authorized-devices="hasAuthorizedDevices"
      test-id-prefix="capacity-detail"
      @update:process-id="selectProcess"
      @update:device-id="selectDevice"
    />

    <ProductionContextState
      v-if="contextState !== 'ready'"
      :state="contextState"
      :error="contextError"
      test-id-prefix="capacity-detail"
      @retry="initialize"
    />

    <NiondToolbar v-else class="capacity-detail-page__filter-card">
      <div class="capacity-detail-page__filter-row">
        <div class="filter-field">
          <span class="filter-field__label">查询粒度</span>
          <UiRadioGroup v-model:value="queryMode" size="small" @update:value="fetchData">
            <UiRadioButton value="day">按日查询</UiRadioButton>
            <UiRadioButton value="month">按月查询</UiRadioButton>
            <UiRadioButton value="year">按年查询</UiRadioButton>
          </UiRadioGroup>
        </div>

        <div v-if="queryMode === 'day'" class="filter-field">
          <span class="filter-field__label">日期</span>
          <UiDatePicker
            v-model:formatted-value="queryDate"
            value-format="yyyy-MM-dd"
            type="date"
            class="h-10"
            style="width: 180px;"
            @update:formatted-value="fetchData"
          />
        </div>
        <div v-if="queryMode === 'month'" class="filter-field">
          <span class="filter-field__label">月份</span>
          <UiDatePicker
            v-model:formatted-value="queryMonth"
            value-format="yyyy-MM"
            type="month"
            class="h-10"
            style="width: 180px;"
            @update:formatted-value="fetchData"
          />
        </div>
        <div v-if="queryMode === 'year'" class="filter-field">
          <span class="filter-field__label">年份</span>
          <UiSelect
            v-model:value="queryYear"
            :options="yearOptions"
            size="small"
            style="width: 130px;"
            @update:value="fetchData"
          />
        </div>
        <div class="filter-field">
          <span class="filter-field__label">PLC 范围</span>
          <UiSelect
            v-model:value="plcCodeFilter"
            :options="plcOptions"
            placeholder="全部 PLC"
            size="small"
            clearable
            style="width: 200px;"
          />
        </div>
      </div>
    </NiondToolbar>

    <NiondTableCard v-if="contextState === 'ready' && loading" class="capacity-detail-page__state-card">
      <LoadingState variant="card" :rows="5" />
    </NiondTableCard>
    <NiondTableCard v-else-if="contextState === 'ready' && loadError" class="capacity-detail-page__state-card">
      <EmptyState :title="loadError.title" :description="loadError.message">
        <template #action>
          <UiButton size="small" type="primary" @click="fetchData">重新加载</UiButton>
        </template>
      </EmptyState>
    </NiondTableCard>
    <NiondTableCard v-else-if="contextState === 'ready' && rows.length === 0" class="capacity-detail-page__state-card">
      <EmptyState
        title="当前范围没有完工弹夹明细"
        description="当前日期与 PLC 筛选下没有可展示的数据。"
      />
    </NiondTableCard>
    <template v-else-if="contextState === 'ready'">
      <div class="capacity-detail-page__stats">
        <StatCard label="完工弹夹数" :value="formatInt(summary.total)" unit="个" accent="brand" />
        <StatCard label="合格弹夹数" :value="formatInt(summary.ok)" unit="个" accent="success" />
        <StatCard label="不合格弹夹数" :value="formatInt(summary.ng)" unit="个" accent="error" />
        <StatCard
          label="良率"
          :value="summary.ratePercent === null ? '—' : summary.ratePercent.toFixed(1)"
          :unit="summary.ratePercent === null ? '' : '%'"
          :accent="summary.ratePercent === null ? 'info' : rateAccent(summary.ratePercent)"
        />
      </div>
      <CapacityTrendChart
        :chart-option="chartOption"
        :chart-subtitle="chartSubtitle"
      />
      <NiondTableCard class="capacity-detail-page__table-card">
        <UiDataTable
          class="capacity-detail-page__table"
          :columns="columns"
          :data="rows"
          :row-key="rowKey"
        />
      </NiondTableCard>
    </template>
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { ChevronLeft, Download } from 'lucide-vue-next';
import StatCard from '../../components/data/StatCard.vue';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiDatePicker from '../../components/ui/UiDatePicker.vue';
import UiRadioButton from '../../components/ui/UiRadioButton.vue';
import UiRadioGroup from '../../components/ui/UiRadioGroup.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import { ProductionContextState, ProductionContextToolbar } from '../../shared/production-context';
import CapacityTrendChart from './CapacityTrendChart.vue';
import { useCapacityDetail } from './useCapacityDetail';
import './capacity-page.css';

const router = useRouter();
const {
  deviceName,
  processOptions,
  deviceOptions,
  selectedProcessId,
  selectedDeviceId,
  context,
  contextStatus,
  contextError,
  contextState,
  hasAuthorizedDevices,
  queryMode,
  queryDate,
  queryMonth,
  queryYear,
  plcCodeFilter,
  yearOptions,
  plcOptions,
  loading,
  loadError,
  rows,
  summary,
  subtitleText,
  chartSubtitle,
  chartOption,
  columns,
  rowKey,
  canExport,
  initialize,
  selectProcess,
  selectDevice,
  fetchData,
  exportRows,
  formatInt,
  rateAccent,
} = useCapacityDetail();

onMounted(initialize);
</script>
