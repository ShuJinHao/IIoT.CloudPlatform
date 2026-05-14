<template>
  <NiondDataPage
    class="detail-page"
      :title="deviceName"
      :subtitle="subtitleText"
  >
      <template #actions>
        <UiButton quaternary size="small" @click="router.back()">
          <template #icon>
            <svg viewBox="0 0 16 16" fill="none" width="14" height="14">
              <path d="M10 3L5 8l5 5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
          </template>
          返回
        </UiButton>
      </template>

    <!-- 查询控制栏 -->
    <NiondToolbar class="detail-page__filter-card">
      <div class="detail-page__filter-row">
        <div class="filter-field">
          <span class="filter-field__label">查询粒度</span>
          <UiRadioGroup v-model:value="queryMode" size="small" @update:value="onModeChange">
            <UiRadioButton value="day">按日查询</UiRadioButton>
            <UiRadioButton value="month">按月查询</UiRadioButton>
            <UiRadioButton value="year">按年查询</UiRadioButton>
          </UiRadioGroup>
        </div>

        <div class="filter-field" v-if="queryMode === 'day'">
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

        <div class="filter-field" v-if="queryMode === 'month'">
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

        <div class="filter-field" v-if="queryMode === 'year'">
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

    <!-- 5 个统计卡 -->
    <div class="detail-page__stats">
      <StatCard
        label="总产出"
        :value="formatInt(summary.total)"
        unit="件"
        accent="brand"
      />
      <StatCard
        label="良品"
        :value="formatInt(summary.ok)"
        unit="件"
        accent="success"
      />
      <StatCard
        label="不良品"
        :value="formatInt(summary.ng)"
        unit="件"
        accent="error"
      />
      <StatCard
        label="良率"
        :value="summary.ratePercent.toFixed(1)"
        unit="%"
        :accent="rateAccent(summary.ratePercent)"
      />
      <StatCard
        :label="avgLabel"
        :value="formatInt(summary.avg)"
        unit="件"
        accent="info"
      />
    </div>

    <!-- 柱状图 -->
    <CardSurface
      class="detail-page__chart-card"
      title="产能趋势图"
      :subtitle="chartSubtitle"
    >
      <div class="detail-page__chart-wrap">
        <LoadingState v-if="loading" variant="card" :rows="4" />
        <EmptyState
          v-else-if="rows.length === 0"
          title="该时段暂无产能数据"
        />
        <v-chart
          v-else
          class="detail-page__chart"
          :option="chartOption"
          autoresize
        />
      </div>
    </CardSurface>

    <!-- 明细表格 -->
    <NiondTableCard class="detail-page__table-card">
      <UiDataTable
        class="detail-page__table"
        :columns="columns"
        :data="rows"
        :loading="loading"
        :bordered="false"
        :single-line="false"
        :row-key="rowKey"
        size="small"
      />
    </NiondTableCard>
  </NiondDataPage>
</template>

<script setup lang="ts">
import { ref, computed, h, onMounted } from 'vue';
import '../../components/charts/echartsSetup';
import {
  getHourlyByDeviceApi,
  getDailySummaryApi,
  getSummaryRangeApi,
} from '../../api/capacity';
import StatCard from '../../components/data/StatCard.vue';
import CardSurface from '../../components/layout/CardSurface.vue';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiDatePicker from '../../components/ui/UiDatePicker.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiRadioButton from '../../components/ui/UiRadioButton.vue';
import UiRadioGroup from '../../components/ui/UiRadioGroup.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import { useRouter, useRoute } from 'vue-router';

const route = useRoute();
const router = useRouter();

const deviceId = ref((route.query.deviceId as string | undefined) ?? '');
const deviceName = ref(
  (route.query.deviceName as string | undefined) ?? '设备详情',
);

// === 查询模式 ===
type QueryMode = 'day' | 'month' | 'year';
const queryMode = ref<QueryMode>('day');

const todayLocal = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};
const thisMonth = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
};

const queryDate = ref(todayLocal());
const queryMonth = ref(thisMonth());
const queryYear = ref<number>(new Date().getFullYear());
const yearOptions = Array.from({ length: 5 }, (_, i) => {
  const y = new Date().getFullYear() - i;
  return { label: `${y} 年`, value: y };
});
const plcNameFilter = ref('');

// === 数据 ===
const loading = ref(false);

interface Row {
  label: string;
  shift: string;
  total: number;
  ok: number;
  ng: number;
  rate: number;
}
const rows = ref<Row[]>([]);

const summary = computed(() => {
  const total = rows.value.reduce((s, r) => s + r.total, 0);
  const ok = rows.value.reduce((s, r) => s + r.ok, 0);
  const ng = rows.value.reduce((s, r) => s + r.ng, 0);
  const ratePercent = total > 0 ? (ok * 100) / total : 0;
  const div =
    queryMode.value === 'year' ? 12 : Math.max(1, rows.value.length);
  const avg = Math.round(total / div);
  return { total, ok, ng, ratePercent, avg };
});

const avgLabel = computed(() => {
  if (queryMode.value === 'year') return '月均产出';
  if (queryMode.value === 'month') return '日均产出';
  return '半小时均产';
});

const subtitleText = computed(() => {
  let base = '产能详细报表 · 年 / 月 / 日 三级查询';
  if (plcNameFilter.value) {
    base += ` · PLC: ${plcNameFilter.value}`;
  }
  return base;
});

const chartSubtitle = computed(() => {
  if (queryMode.value === 'day')
    return `按时间段统计 · ${rows.value.length} 个数据点`;
  if (queryMode.value === 'month') return `按日统计 · ${queryDate.value}`;
  return `按月统计 · ${queryYear.value} 年`;
});

const tableTimeLabel = computed(() => {
  if (queryMode.value === 'day') return '时间段';
  if (queryMode.value === 'month') return '日期';
  return '月份';
});

const rateAccent = (
  rate: number,
): 'success' | 'warn' | 'error' => {
  if (rate >= 95) return 'success';
  if (rate >= 85) return 'warn';
  return 'error';
};

const formatInt = (n: number) => n.toLocaleString('zh-CN');

// === ECharts 选项（堆叠柱状） ===
const chartOption = computed(() => {
  const xAxis = rows.value.map((r) => r.label);
  const okData = rows.value.map((r) => r.ok);
  const ngData = rows.value.map((r) => r.ng);
  return {
    grid: { left: 48, right: 16, top: 32, bottom: 36 },
    legend: {
      data: ['良品', '不良品'],
      top: 0,
      right: 8,
      itemWidth: 12,
      itemHeight: 8,
      textStyle: { color: '#6b7384', fontSize: 12 },
    },
    tooltip: {
      trigger: 'axis',
      backgroundColor: 'rgba(255, 255, 255, 0.98)',
      borderColor: 'rgba(15, 23, 42, 0.08)',
      borderWidth: 1,
      extraCssText: 'box-shadow: 0 4px 16px rgba(15, 23, 42, 0.08);',
      textStyle: {
        color: '#1a1d29',
        fontFamily: "'Inter', sans-serif",
        fontSize: 12,
      },
      axisPointer: { type: 'shadow' },
    },
    xAxis: {
      type: 'category',
      data: xAxis,
      axisLine: { lineStyle: { color: 'rgba(15, 23, 42, 0.08)' } },
      axisLabel: {
        color: '#6b7384',
        fontFamily: "'JetBrains Mono', monospace",
        fontSize: 11,
        rotate: queryMode.value === 'day' && xAxis.length > 12 ? 35 : 0,
        interval: queryMode.value === 'day' && xAxis.length > 24 ? 'auto' : 0,
      },
      axisTick: { show: false },
    },
    yAxis: {
      type: 'value',
      splitLine: { lineStyle: { color: 'rgba(15, 23, 42, 0.05)' } },
      axisLine: { show: false },
      axisTick: { show: false },
      axisLabel: {
        color: '#6b7384',
        fontFamily: "'JetBrains Mono', monospace",
        fontSize: 11,
      },
    },
    series: [
      {
        name: '良品',
        type: 'bar',
        stack: 'total',
        data: okData,
        itemStyle: {
          color: '#0891b2',
          borderRadius: [0, 0, 0, 0],
        },
        barMaxWidth: 36,
      },
      {
        name: '不良品',
        type: 'bar',
        stack: 'total',
        data: ngData,
        itemStyle: {
          color: '#dc2626',
          borderRadius: [4, 4, 0, 0],
        },
        barMaxWidth: 36,
      },
    ],
  };
});

// === 表格列 ===
function renderShiftTag(shift: string) {
  if (!shift) return null;
  return h(
    'span',
    { class: 'shift-tag' },
    shift === 'D' ? '白班' : shift === 'N' ? '夜班' : shift,
  );
}

const columns = computed<UiDataTableColumn<Row>[]>(() => {
  const base: UiDataTableColumn<Row>[] = [
    {
      title: tableTimeLabel.value,
      key: 'label',
      minWidth: 140,
      render(row) {
        return h('span', { class: 'cell-mono' }, row.label);
      },
    },
  ];

  if (queryMode.value === 'day') {
    base.push({
      title: '班次',
      key: 'shift',
      width: 100,
      render(row) {
        return renderShiftTag(row.shift);
      },
    });
  }

  base.push(
    {
      title: '总产出',
      key: 'total',
      align: 'right',
      width: 110,
      render(row) {
        return h('span', { class: 'cell-mono' }, formatInt(row.total));
      },
    },
    {
      title: '良品',
      key: 'ok',
      align: 'right',
      width: 110,
      render(row) {
        return h(
          'span',
          { class: 'cell-mono cell-num--ok' },
          formatInt(row.ok),
        );
      },
    },
    {
      title: '不良品',
      key: 'ng',
      align: 'right',
      width: 110,
      render(row) {
        return h(
          'span',
          { class: 'cell-mono cell-num--ng' },
          formatInt(row.ng),
        );
      },
    },
    {
      title: '良率',
      key: 'rate',
      width: 100,
      align: 'right',
      render(row) {
        const tone = rateAccent(row.rate);
        return h(
          'span',
          { class: ['cell-mono', `cell-num--${tone === 'success' ? 'ok' : tone === 'warn' ? 'warn' : 'ng'}`] },
          `${row.rate.toFixed(1)}%`,
        );
      },
    },
  );

  return base;
});

function rowKey(row: Row) {
  return `${row.label}-${row.shift}`;
}

// === 数据加载 ===
const onModeChange = () => {
  void fetchData();
};

const fetchData = async () => {
  if (!deviceId.value) {
    rows.value = [];
    return;
  }
  loading.value = true;
  rows.value = [];
  try {
    if (queryMode.value === 'day') {
      await fetchDay(queryDate.value);
    } else if (queryMode.value === 'month') {
      await fetchMonth(queryMonth.value);
    } else {
      await fetchYear(queryYear.value);
    }
  } finally {
    loading.value = false;
  }
};

async function fetchDay(date: string) {
  try {
    const hourly = (await getHourlyByDeviceApi({
      deviceId: deviceId.value,
      date,
      plcName: plcNameFilter.value || undefined,
    })) as unknown as any[];
    if (Array.isArray(hourly) && hourly.length > 0) {
      rows.value = hourly.map((h: any) => ({
        label:
          h.timeLabel ??
          h.time_label ??
          h.TimeLabel ??
          `${String(h.hour ?? h.Hour ?? 0).padStart(2, '0')}:${String(h.minute ?? h.Minute ?? 0).padStart(2, '0')}`,
        shift: h.shiftCode ?? h.shift_code ?? h.ShiftCode ?? '',
        total: h.totalCount ?? h.total_count ?? h.TotalCount ?? 0,
        ok: h.okCount ?? h.ok_count ?? h.OkCount ?? 0,
        ng: h.ngCount ?? h.ng_count ?? h.NgCount ?? 0,
        rate:
          (h.totalCount ?? h.total_count ?? h.TotalCount ?? 0) > 0
            ? ((h.okCount ?? h.ok_count ?? h.OkCount ?? 0) /
                (h.totalCount ?? h.total_count ?? h.TotalCount ?? 0)) *
              100
            : 0,
      }));
      return;
    }
  } catch {
    /* 兜底 summary */
  }

  try {
    const s = (await getDailySummaryApi({
      deviceId: deviceId.value,
      date,
      plcName: plcNameFilter.value || undefined,
    })) as any;
    if (!s) return;
    const total = s.totalCount ?? 0;
    const ok = s.okCount ?? 0;
    const ng = s.ngCount ?? 0;
    rows.value = [
      {
        label: '白班 08:30-20:30',
        shift: 'D',
        total: s.dayShiftTotal ?? 0,
        ok: s.dayShiftOk ?? 0,
        ng: s.dayShiftNg ?? 0,
        rate:
          s.dayShiftTotal > 0 ? (s.dayShiftOk / s.dayShiftTotal) * 100 : 0,
      },
      {
        label: '夜班 20:30-08:30',
        shift: 'N',
        total: s.nightShiftTotal ?? 0,
        ok: s.nightShiftOk ?? 0,
        ng: s.nightShiftNg ?? 0,
        rate:
          s.nightShiftTotal > 0
            ? (s.nightShiftOk / s.nightShiftTotal) * 100
            : 0,
      },
    ].filter((r) => r.total > 0);
    if (rows.value.length === 0 && total > 0) {
      rows.value = [
        {
          label: date,
          shift: '-',
          total,
          ok,
          ng,
          rate: total > 0 ? (ok / total) * 100 : 0,
        },
      ];
    }
  } catch {
    /* 无数据 */
  }
}

async function fetchMonth(ym: string) {
  const [year, month] = ym.split('-').map(Number) as [number, number];
  const mm = String(month).padStart(2, '0');
  const lastDay = new Date(year, month, 0).getDate();
  const startDate = `${year}-${mm}-01`;
  const endDate = `${year}-${mm}-${String(lastDay).padStart(2, '0')}`;

  const list = (await getSummaryRangeApi({
    deviceId: deviceId.value,
    startDate,
    endDate,
    plcName: plcNameFilter.value || undefined,
  })) as unknown as any[];
  rows.value = list
    .filter((s: any) => (s.totalCount ?? 0) > 0)
    .map((s: any) => {
      const total = s.totalCount ?? 0;
      const ok = s.okCount ?? 0;
      const ng = s.ngCount ?? 0;
      const d = s.date?.slice(8, 10) ?? '';
      return {
        label: `${mm}-${d}`,
        shift: '',
        total,
        ok,
        ng,
        rate: total > 0 ? (ok / total) * 100 : 0,
      };
    });
}

async function fetchYear(year: number) {
  const startDate = `${year}-01-01`;
  const endDate = `${year}-12-31`;

  const list = (await getSummaryRangeApi({
    deviceId: deviceId.value,
    startDate,
    endDate,
    plcName: plcNameFilter.value || undefined,
  })) as unknown as any[];

  const byMonth: Record<number, { total: number; ok: number; ng: number }> = {};
  for (let m = 1; m <= 12; m++) byMonth[m] = { total: 0, ok: 0, ng: 0 };

  for (const s of list) {
    const m = parseInt((s.date as string).slice(5, 7), 10);
    if (!byMonth[m]) continue;
    byMonth[m].total += s.totalCount ?? 0;
    byMonth[m].ok += s.okCount ?? 0;
    byMonth[m].ng += s.ngCount ?? 0;
  }

  rows.value = Object.entries(byMonth).map(([m, v]) => ({
    label: `${m} 月`,
    shift: '',
    total: v.total,
    ok: v.ok,
    ng: v.ng,
    rate: v.total > 0 ? (v.ok / v.total) * 100 : 0,
  }));
}

onMounted(() => fetchData());
</script>

<style scoped>
.detail-page {
  font-family: var(--font-sans);
  color: var(--text-0);
}

.detail-page__filter-card {
  margin-bottom: var(--space-4);
}
.detail-page__filter-row {
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
  letter-spacing: 0;
}

.detail-page__stats {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: var(--space-4);
  margin-bottom: var(--space-4);
}
@media (max-width: 1280px) {
  .detail-page__stats {
    grid-template-columns: repeat(3, 1fr);
  }
}
@media (max-width: 768px) {
  .detail-page__stats {
    grid-template-columns: repeat(2, 1fr);
  }
}

.detail-page__chart-card {
  margin-bottom: var(--space-4);
}
.detail-page__chart-wrap {
  height: 320px;
  position: relative;
}
.detail-page__chart {
  width: 100%;
  height: 100%;
}

/* 表格单元 */
.detail-page__table :deep(.cell-mono) {
  font-family: var(--font-mono);
  font-size: var(--fs-sm);
  color: var(--text-0);
}
.detail-page__table :deep(.cell-num--ok) {
  color: var(--success);
}
.detail-page__table :deep(.cell-num--ng) {
  color: var(--error);
}
.detail-page__table :deep(.cell-num--warn) {
  color: var(--warn);
}

/* 班次徽章 */
.detail-page__table :deep(.shift-tag) {
  font-size: var(--fs-xs);
  background: var(--brand-soft);
  color: var(--brand);
  padding: 2px 8px;
  border-radius: var(--radius-sm);
  font-weight: var(--fw-display);
  font-family: var(--font-mono);
}

/* 项目自有数据表微调 */
.detail-page__table :deep(.n-data-table-thead) {
  background: var(--bg-3);
}
.detail-page__table :deep(.n-data-table-th) {
  font-size: var(--fs-xs) !important;
  font-weight: var(--fw-semibold) !important;
  color: var(--text-2) !important;
  letter-spacing: 0;
  text-transform: uppercase;
}
.detail-page__table :deep(.n-data-table-tr:hover .n-data-table-td) {
  background-color: var(--bg-3) !important;
}
</style>
