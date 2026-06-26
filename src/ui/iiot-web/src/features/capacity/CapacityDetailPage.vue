<template>
  <NiondDataPage
    class="capacity-detail-page"
    :title="deviceName"
    :subtitle="subtitleText"
  >
    <template #actions>
      <UiButton quaternary size="small" @click="router.back()">
        <template #icon><ChevronLeft :size="14" /></template>
        返回
      </UiButton>
    </template>

    <NiondToolbar class="capacity-detail-page__filter-card">
      <div class="capacity-detail-page__filter-row">
        <div class="filter-field">
          <span class="filter-field__label">查询粒度</span>
          <UiRadioGroup v-model:value="queryMode" size="small" @update:value="onModeChange">
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
            size="small"
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
            size="small"
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
          <span class="filter-field__label">PLC 名称（可选）</span>
          <UiInput
            v-model:value="plcNameFilter"
            placeholder="不填查全部"
            size="small"
            clearable
            style="width: 200px;"
            @keyup.enter="fetchData"
            @clear="fetchData"
          />
        </div>
      </div>
    </NiondToolbar>

    <div class="capacity-detail-page__stats">
      <StatCard label="总产出" :value="formatInt(summary.total)" unit="件" accent="brand" />
      <StatCard label="良品" :value="formatInt(summary.ok)" unit="件" accent="success" />
      <StatCard label="不良品" :value="formatInt(summary.ng)" unit="件" accent="error" />
      <StatCard label="良率" :value="summary.ratePercent.toFixed(1)" unit="%" :accent="rateAccent(summary.ratePercent)" />
      <StatCard :label="avgLabel" :value="formatInt(summary.avg)" unit="件" accent="info" />
    </div>

    <CapacityTrendChart
      :loading="loading"
      :rows="rows"
      :chart-option="chartOption"
      :chart-subtitle="chartSubtitle"
    />

    <NiondTableCard class="capacity-detail-page__table-card">
      <UiDataTable
        class="capacity-detail-page__table"
        :columns="columns"
        :data="rows"
        :loading="loading"
        :row-key="rowKey"
      >
        <template #empty>
          <EmptyState title="该时段暂无明细数据" />
        </template>
      </UiDataTable>
    </NiondTableCard>
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { ChevronLeft } from 'lucide-vue-next';
import StatCard from '../../components/data/StatCard.vue';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiDatePicker from '../../components/ui/UiDatePicker.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiRadioButton from '../../components/ui/UiRadioButton.vue';
import UiRadioGroup from '../../components/ui/UiRadioGroup.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import CapacityTrendChart from './CapacityTrendChart.vue';
import { useCapacityDetail } from './useCapacityDetail';
import './capacity-page.css';

const router = useRouter();
const {
  deviceName,
  queryMode,
  queryDate,
  queryMonth,
  queryYear,
  plcNameFilter,
  yearOptions,
  loading,
  rows,
  summary,
  avgLabel,
  subtitleText,
  chartSubtitle,
  chartOption,
  columns,
  rowKey,
  fetchData,
  formatInt,
  rateAccent,
  onModeChange,
} = useCapacityDetail();

onMounted(fetchData);
</script>
