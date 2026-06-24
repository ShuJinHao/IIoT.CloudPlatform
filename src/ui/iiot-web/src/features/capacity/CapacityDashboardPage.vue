<template>
  <NiondDataPage
    class="capacity-page"
    page-key="capacity"
    title="产能看板"
    subtitle="当前权限范围内每日产能汇总，点击「查看详情」进入设备级报表"
  >
    <div class="capacity-page__stats">
      <StatCard label="本页总产出" :value="formatInt(totalStats.total)" unit="件" accent="brand" />
      <StatCard label="本页良品" :value="formatInt(totalStats.ok)" unit="件" accent="success" />
      <StatCard label="本页不良品" :value="formatInt(totalStats.ng)" unit="件" accent="error" />
      <StatCard
        label="本页综合良率"
        :value="totalStats.ratePercent.toFixed(1)"
        unit="%"
        :accent="rateAccent(totalStats.ratePercent)"
      />
    </div>

    <NiondToolbar class="capacity-page__filter-card">
      <div class="capacity-page__filter-row">
        <div class="filter-field">
          <span class="filter-field__label">设备</span>
          <UiSelect
            v-model:value="deviceFilter"
            :options="deviceOptions"
            placeholder="全部设备"
            clearable
            size="small"
            style="width: 220px;"
            @update:value="onFilterChange"
          />
        </div>
        <div class="filter-field">
          <span class="filter-field__label">日期</span>
          <UiDatePicker
            v-model:formatted-value="dateFilter"
            value-format="yyyy-MM-dd"
            type="date"
            size="small"
            style="width: 180px;"
            @update:formatted-value="onFilterChange"
          />
        </div>
        <UiButton quaternary size="small" @click="clearFilters">
          <template #icon><X :size="14" /></template>
          清空筛选
        </UiButton>
      </div>
      <div v-if="deviceLoadError" class="capacity-page__filter-error">
        {{ deviceLoadError }}
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
          <EmptyState title="暂无产能数据" description="当前筛选条件下没有可展示的产能汇总。" />
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
import UiSelect from '../../components/ui/UiSelect.vue';
import { createCapacityDashboardColumns } from './columns';
import { useCapacityDashboard } from './useCapacityDashboard';
import { formatInt, rateAccent } from './types';
import './capacity-page.css';

const {
  records,
  loading,
  currentPage,
  metaData,
  deviceFilter,
  dateFilter,
  deviceOptions,
  deviceLoadError,
  totalStats,
  initialize,
  onFilterChange,
  clearFilters,
  onPageChange,
  goDetail,
  rowKey,
} = useCapacityDashboard();

const columns = createCapacityDashboardColumns({ onDetail: goDetail });

onMounted(initialize);
</script>
