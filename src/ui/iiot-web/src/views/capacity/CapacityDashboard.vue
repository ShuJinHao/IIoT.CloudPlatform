<template>
  <div class="capacity-page">
    <PageHeader
      title="产能看板"
      subtitle="所有机台每日产能汇总，点击「查看详情」进入设备级报表"
    />

    <div class="capacity-page__stats">
      <StatCard
        label="本页总产出"
        :value="formatInt(totalStats.total)"
        unit="件"
        accent="brand"
      />
      <StatCard
        label="本页良品"
        :value="formatInt(totalStats.ok)"
        unit="件"
        accent="success"
      />
      <StatCard
        label="本页不良品"
        :value="formatInt(totalStats.ng)"
        unit="件"
        accent="error"
      />
      <StatCard
        label="本页综合良率"
        :value="totalStats.ratePercent.toFixed(1)"
        unit="%"
        :accent="rateAccent(totalStats.ratePercent)"
      />
    </div>

    <CardSurface class="capacity-page__filter-card">
      <div class="capacity-page__filter-row">
        <div class="filter-field">
          <span class="filter-field__label">设备</span>
          <n-select
            v-model:value="deviceFilter"
            :options="deviceOptions"
            placeholder="全部设备"
            clearable
            filterable
            size="small"
            style="width: 220px;"
            @update:value="onFilterChange"
          />
        </div>
        <div class="filter-field">
          <span class="filter-field__label">日期</span>
          <n-date-picker
            v-model:formatted-value="dateFilter"
            value-format="yyyy-MM-dd"
            type="date"
            size="small"
            style="width: 180px;"
            @update:formatted-value="onFilterChange"
          />
        </div>
        <n-button quaternary size="small" @click="clearFilters">
          清空筛选
        </n-button>
      </div>
    </CardSurface>

    <CardSurface class="capacity-page__table-card" no-padding>
      <n-data-table
        class="capacity-page__table"
        :columns="columns"
        :data="records"
        :loading="loading"
        :bordered="false"
        :single-line="false"
        :row-key="rowKey"
        size="small"
      />

      <div v-if="metaData.totalPages > 1" class="capacity-page__pagination">
        <n-pagination
          :page="currentPage"
          :page-count="metaData.totalPages"
          :item-count="metaData.totalCount"
          :page-size="10"
          show-quick-jumper
          @update:page="onPageChange"
        />
      </div>
    </CardSurface>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, h, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import {
  NSelect,
  NDatePicker,
  NButton,
  NDataTable,
  NPagination,
} from 'naive-ui';
import type { DataTableColumns } from 'naive-ui';
import { getDailyPagedApi, type DailyCapacityItem } from '../../api/capacity';
import { getAllActiveDevicesApi, type DeviceSelectDto } from '../../api/device';
import type { PagedMetaData } from '../../api/employee';
import PageHeader from '../../components/layout/PageHeader.vue';
import StatCard from '../../components/data/StatCard.vue';
import CardSurface from '../../components/layout/CardSurface.vue';

const router = useRouter();

// === 状态 ===
const records = ref<DailyCapacityItem[]>([]);
const loading = ref(false);
const allDevices = ref<DeviceSelectDto[]>([]);
const metaData = ref<PagedMetaData>({
  totalCount: 0,
  pageSize: 10,
  currentPage: 1,
  totalPages: 1,
});
const currentPage = ref(1);

// === 筛选 ===
const todayLocal = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

const deviceFilter = ref<string | null>(null);
const dateFilter = ref<string>(todayLocal());

const deviceOptions = computed(() =>
  allDevices.value.map((d) => ({
    label: d.deviceName,
    value: d.id,
  })),
);

// === 统计聚合（仅当前页） ===
const totalStats = computed(() => {
  const total = records.value.reduce((s, r) => s + r.totalCount, 0);
  const ok = records.value.reduce((s, r) => s + r.okCount, 0);
  const ng = records.value.reduce((s, r) => s + r.ngCount, 0);
  const ratePercent = total > 0 ? (ok * 100) / total : 0;
  return { total, ok, ng, ratePercent };
});

const rateAccent = (
  rate: number,
): 'success' | 'warn' | 'error' => {
  if (rate >= 95) return 'success';
  if (rate >= 85) return 'warn';
  return 'error';
};

const formatInt = (n: number) => n.toLocaleString('zh-CN');

// === 良率 cell 自定义渲染 ===
function renderRateBar(rate: number) {
  const tone = rateAccent(rate);
  return h('div', { class: 'rate-cell' }, [
    h('div', { class: 'rate-cell__track' }, [
      h('div', {
        class: ['rate-cell__bar', `rate-cell__bar--${tone}`],
        style: { width: `${Math.min(100, rate)}%` },
      }),
    ]),
    h(
      'span',
      { class: ['rate-cell__text', `rate-cell__text--${tone}`] },
      `${rate.toFixed(1)}%`,
    ),
  ]);
}

// === 表格列定义 ===
const columns: DataTableColumns<DailyCapacityItem> = [
  {
    title: '设备',
    key: 'deviceName',
    minWidth: 180,
    render(row) {
      return h('span', { class: 'cell-device' }, row.deviceName);
    },
  },
  {
    title: '日期',
    key: 'date',
    width: 130,
    render(row) {
      return h('span', { class: 'cell-mono' }, row.date);
    },
  },
  {
    title: '总产出',
    key: 'totalCount',
    align: 'right',
    width: 120,
    render(row) {
      return h('span', { class: 'cell-mono' }, formatInt(row.totalCount));
    },
  },
  {
    title: '良品',
    key: 'okCount',
    align: 'right',
    width: 110,
    render(row) {
      return h(
        'span',
        { class: 'cell-mono cell-num--ok' },
        formatInt(row.okCount),
      );
    },
  },
  {
    title: '不良品',
    key: 'ngCount',
    align: 'right',
    width: 110,
    render(row) {
      return h(
        'span',
        { class: 'cell-mono cell-num--ng' },
        formatInt(row.ngCount),
      );
    },
  },
  {
    title: '良率',
    key: 'okRate',
    minWidth: 180,
    render(row) {
      return renderRateBar(row.okRate);
    },
  },
  {
    title: '操作',
    key: 'actions',
    width: 110,
    align: 'center',
    render(row) {
      return h(
        NButton,
        {
          size: 'tiny',
          type: 'primary',
          secondary: true,
          disabled: !row.deviceId,
          onClick: () => goDetail(row.deviceId, row.deviceName),
        },
        { default: () => '查看详情' },
      );
    },
  },
];

// === 数据加载 ===
async function fetchData() {
  loading.value = true;
  try {
    const result = await getDailyPagedApi({
      PageNumber: currentPage.value,
      PageSize: 10,
      date: dateFilter.value || undefined,
      deviceId: deviceFilter.value || undefined,
    });
    records.value = result.items;
    metaData.value = result.metaData;
  } catch {
    records.value = [];
    metaData.value = {
      totalCount: 0,
      pageSize: 10,
      currentPage: 1,
      totalPages: 1,
    };
  } finally {
    loading.value = false;
  }
}

function onFilterChange() {
  currentPage.value = 1;
  void fetchData();
}

function clearFilters() {
  deviceFilter.value = null;
  dateFilter.value = todayLocal();
  currentPage.value = 1;
  void fetchData();
}

function onPageChange(p: number) {
  currentPage.value = p;
  void fetchData();
}

function goDetail(deviceId: string, deviceName: string) {
  if (!deviceId) return;
  void router.push({
    name: 'CapacityDetail',
    query: { deviceId, deviceName },
  });
}

function rowKey(row: DailyCapacityItem) {
  return `${row.deviceId}-${row.date}`;
}

onMounted(async () => {
  try {
    allDevices.value = await getAllActiveDevicesApi();
  } catch {
    allDevices.value = [];
  }
  await fetchData();
});
</script>

<style scoped>
.capacity-page {
  font-family: var(--font-sans);
  color: var(--text-0);
}

/* === KPI 网格 === */
.capacity-page__stats {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: var(--space-4);
  margin-bottom: var(--space-5);
}

@media (max-width: 1100px) {
  .capacity-page__stats {
    grid-template-columns: repeat(2, 1fr);
  }
}
@media (max-width: 600px) {
  .capacity-page__stats {
    grid-template-columns: 1fr;
  }
}

/* === 筛选卡 === */
.capacity-page__filter-card {
  margin-bottom: var(--space-4);
}
.capacity-page__filter-row {
  display: flex;
  align-items: flex-end;
  gap: var(--space-4);
  flex-wrap: wrap;
}
.filter-field {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}
.filter-field__label {
  font-size: var(--fs-xs);
  color: var(--text-2);
  font-weight: var(--fw-medium);
  letter-spacing: 0.5px;
}

/* === 表格卡 === */
.capacity-page__table-card {
  /* CardSurface 已设了 border-radius/border，no-padding 让 NDataTable 撑到边 */
}
.capacity-page__pagination {
  display: flex;
  justify-content: flex-end;
  padding: var(--space-4);
  border-top: 1px solid var(--border);
}

/* === 表格单元 === */
.capacity-page__table :deep(.cell-device) {
  font-size: var(--fs-base);
  font-weight: var(--fw-medium);
  color: var(--text-0);
}
.capacity-page__table :deep(.cell-mono) {
  font-family: var(--font-mono);
  font-size: var(--fs-sm);
  color: var(--text-0);
}
.capacity-page__table :deep(.cell-num--ok) {
  color: var(--success);
}
.capacity-page__table :deep(.cell-num--ng) {
  color: var(--error);
}

/* === 良率进度条 === */
.capacity-page__table :deep(.rate-cell) {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 140px;
}
.capacity-page__table :deep(.rate-cell__track) {
  flex: 1;
  height: 6px;
  background: rgba(255, 255, 255, 0.06);
  border-radius: 3px;
  overflow: hidden;
  min-width: 60px;
}
.capacity-page__table :deep(.rate-cell__bar) {
  height: 100%;
  border-radius: 3px;
  transition: width var(--motion-base);
  box-shadow: 0 0 8px currentColor;
}
.capacity-page__table :deep(.rate-cell__bar--success) {
  background: var(--success);
  color: var(--success);
}
.capacity-page__table :deep(.rate-cell__bar--warn) {
  background: var(--warn);
  color: var(--warn);
}
.capacity-page__table :deep(.rate-cell__bar--error) {
  background: var(--error);
  color: var(--error);
}
.capacity-page__table :deep(.rate-cell__text) {
  font-family: var(--font-mono);
  font-size: var(--fs-sm);
  font-weight: var(--fw-semibold);
  white-space: nowrap;
}
.capacity-page__table :deep(.rate-cell__text--success) {
  color: var(--success);
}
.capacity-page__table :deep(.rate-cell__text--warn) {
  color: var(--warn);
}
.capacity-page__table :deep(.rate-cell__text--error) {
  color: var(--error);
}

/* === Naive UI DataTable 微调（贴合 hybrid 风） === */
.capacity-page__table :deep(.n-data-table-thead) {
  background: var(--bg-1);
}
.capacity-page__table :deep(.n-data-table-th) {
  font-size: var(--fs-xs) !important;
  font-weight: var(--fw-semibold) !important;
  color: var(--text-2) !important;
  letter-spacing: 1px;
  text-transform: uppercase;
}
.capacity-page__table :deep(.n-data-table-tr:hover .n-data-table-td) {
  background-color: rgba(8, 145, 178, 0.04) !important;
}
</style>
