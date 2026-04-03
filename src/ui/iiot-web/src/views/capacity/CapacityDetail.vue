<template>
  <div class="detail-page">

    <!-- 页头 -->
    <div class="page-header">
      <button class="back-btn" @click="router.back()">
        <svg viewBox="0 0 16 16" fill="none"><path d="M10 3L5 8l5 5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>
        返回
      </button>
      <div>
        <h1 class="page-title">{{ deviceName }}</h1>
        <p class="page-sub">产能详细报表 · 年 / 月 / 日 三级查询</p>
      </div>
    </div>

    <!-- 查询控制栏 -->
    <div class="query-bar">
      <div class="mode-tabs">
        <button v-for="m in modes" :key="m.value"
          class="mode-tab" :class="{ active: queryMode === m.value }"
          @click="switchMode(m.value)">{{ m.label }}</button>
      </div>
      <div class="date-inputs">
        <template v-if="queryMode === 'day'">
          <input type="date" v-model="queryDate" class="filter-input" @change="fetchData" />
        </template>
        <template v-if="queryMode === 'month'">
          <input type="month" v-model="queryMonth" class="filter-input" @change="fetchData" />
        </template>
        <template v-if="queryMode === 'year'">
          <select v-model="queryYear" class="filter-input" @change="fetchData">
            <option v-for="y in yearOptions" :key="y" :value="y">{{ y }} 年</option>
          </select>
        </template>
      </div>
    </div>

    <!-- 汇总卡片 -->
    <div class="stat-cards">
      <div class="stat-card">
        <span class="stat-label">总产出</span>
        <span class="stat-value">{{ summary.total }}</span>
      </div>
      <div class="stat-card ok">
        <span class="stat-label">良品</span>
        <span class="stat-value">{{ summary.ok }}</span>
      </div>
      <div class="stat-card ng">
        <span class="stat-label">不良品</span>
        <span class="stat-value">{{ summary.ng }}</span>
      </div>
      <div class="stat-card rate">
        <span class="stat-label">良率</span>
        <span class="stat-value">{{ summary.rate }}</span>
      </div>
      <div class="stat-card avg">
        <span class="stat-label">{{ avgLabel }}</span>
        <span class="stat-value">{{ summary.avg }}</span>
      </div>
    </div>

    <!-- 柱状图 -->
    <div class="chart-card">
      <div class="chart-header">
        <span class="chart-title">产能趋势图</span>
        <div class="chart-legend">
          <span class="legend-item"><span class="legend-dot ok"></span>良品</span>
          <span class="legend-item"><span class="legend-dot ng"></span>不良品</span>
        </div>
      </div>
      <div v-if="loading" class="chart-loading">
        <div class="loading-ring"></div>
      </div>
      <div v-else-if="rows.length > 0" class="chart-area">
        <div class="chart-bars" :class="{ 'dense-labels': queryMode === 'day' }">
          <div class="chart-col" v-for="(row, index) in rows" :key="row.label">
            <span class="bar-total">{{ row.total }}</span>
            <div class="bar-stack">
              <div class="bar ng" :style="{ height: barH(row.ng) + 'px' }" :title="'不良: ' + row.ng"></div>
              <div class="bar ok" :style="{ height: barH(row.ok) + 'px' }" :title="'良品: ' + row.ok"></div>
            </div>
            <span v-if="shouldShowBarLabel(index)" class="bar-label" :title="row.label">{{ formatBarLabel(row.label) }}</span>
            <span v-else class="bar-label placeholder"></span>
          </div>
        </div>
      </div>
      <div v-else class="chart-empty">暂无数据</div>
    </div>

    <!-- 明细表格 -->
    <div class="table-wrap">
      <div v-if="loading" class="skeleton-rows">
        <div v-for="i in 6" :key="i" class="skeleton-row">
          <div class="skel skel-md"></div>
          <div class="skel skel-sm"></div>
          <div class="skel skel-sm"></div>
          <div class="skel skel-sm"></div>
        </div>
      </div>
      <table v-else-if="rows.length > 0" class="data-table">
        <thead>
          <tr>
            <th>{{ tableTimeLabel }}</th>
            <th v-if="queryMode === 'day'">班次</th>
            <th>总产出</th>
            <th>良品</th>
            <th>不良品</th>
            <th>良率</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in rows" :key="row.label" class="table-row">
            <td class="mono">{{ row.label }}</td>
            <td v-if="queryMode === 'day'">
              <span class="shift-tag">{{ row.shift }}</span>
            </td>
            <td class="mono">{{ row.total }}</td>
            <td class="mono ok-num">{{ row.ok }}</td>
            <td class="mono ng-num">{{ row.ng }}</td>
            <td>
              <span class="mono" :class="rateClass(row.rate)">{{ row.rate.toFixed(1) }}%</span>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-else class="empty-cell">
        <div class="empty-state"><p>该时段暂无产能数据</p></div>
      </div>
    </div>

  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { getHourlyByDeviceApi, getDailySummaryApi, getSummaryRangeApi } from '../../api/capacity';

const route  = useRoute();
const router = useRouter();

const deviceId   = ref(route.query.deviceId   as string ?? '');
const deviceName = ref(route.query.deviceName as string ?? '设备详情');

// ── 查询模式 ────────────────────────────────────────────────────────
const modes = [
  { value: 'day',   label: '按日查询' },
  { value: 'month', label: '按月查询' },
  { value: 'year',  label: '按年查询' },
] as const;
const queryMode = ref<'day' | 'month' | 'year'>('day');

const todayLocal = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
};
const thisMonth = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}`;
};

const queryDate  = ref(todayLocal());
const queryMonth = ref(thisMonth());
const queryYear  = ref(new Date().getFullYear());
const yearOptions = Array.from({ length: 5 }, (_, i) => new Date().getFullYear() - i);

// ── 数据 ────────────────────────────────────────────────────────────
const loading = ref(false);

interface Row {
  label: string;
  shift: string;
  total: number;
  ok:    number;
  ng:    number;
  rate:  number;
}
const rows = ref<Row[]>([]);

const summary = computed(() => {
  const total = rows.value.reduce((s, r) => s + r.total, 0);
  const ok    = rows.value.reduce((s, r) => s + r.ok,    0);
  const ng    = rows.value.reduce((s, r) => s + r.ng,    0);
  const rate  = total > 0 ? (ok * 100 / total).toFixed(1) + '%' : '0%';
  const div   = queryMode.value === 'year' ? 12 : Math.max(1, rows.value.length);
  const avg   = Math.round(total / div);
  return { total, ok, ng, rate, avg };
});

const avgLabel = computed(() => {
  if (queryMode.value === 'year')  return '月均产出';
  if (queryMode.value === 'month') return '日均产出';
  return '半小时均产';
});

const tableTimeLabel = computed(() => {
  if (queryMode.value === 'day')   return '时间段';
  if (queryMode.value === 'month') return '日期';
  return '月份';
});

// ── 图表 ────────────────────────────────────────────────────────────
const maxTotal = computed(() => Math.max(...rows.value.map(r => r.total), 1));
const barH = (val: number) => Math.max(2, Math.round((val / maxTotal.value) * 150));

const chartLabelStep = computed(() => {
  if (queryMode.value !== 'day') return 1;
  if (rows.value.length > 36) return 4;
  if (rows.value.length > 24) return 3;
  return 2;
});

const shouldShowBarLabel = (index: number) => {
  if (queryMode.value !== 'day') return true;
  return index % chartLabelStep.value === 0 || index === rows.value.length - 1;
};

const formatBarLabel = (label: string) => {
  if (queryMode.value !== 'day') return label;
  return label.slice(0, 5);
};

const rateClass = (rate: number) => {
  if (rate >= 95) return 'rate-good';
  if (rate >= 85) return 'rate-warn';
  return 'rate-bad';
};

// ── 切换模式 ────────────────────────────────────────────────────────
const switchMode = (mode: 'day' | 'month' | 'year') => {
  queryMode.value = mode;
  fetchData();
};

// ── 核心查询 ────────────────────────────────────────────────────────
const fetchData = async () => {
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

// 日查询：优先 hourly 明细，兜底 summary
const fetchDay = async (date: string) => {
  try {
    const hourly = await getHourlyByDeviceApi({ deviceId: deviceId.value, date }) as unknown as any[];
    if (Array.isArray(hourly) && hourly.length > 0) {
      rows.value = hourly.map((h: any) => ({
        label: h.timeLabel   ?? h.time_label ?? h.TimeLabel ?? `${String(h.hour ?? h.Hour ?? 0).padStart(2,'0')}:${String(h.minute ?? h.Minute ?? 0).padStart(2,'0')}`,
        shift: h.shiftCode   ?? h.shift_code ?? h.ShiftCode ?? '',
        total: h.totalCount  ?? h.total_count ?? h.TotalCount ?? 0,
        ok:    h.okCount     ?? h.ok_count ?? h.OkCount ?? 0,
        ng:    h.ngCount     ?? h.ng_count ?? h.NgCount ?? 0,
        rate:  (h.totalCount ?? h.total_count ?? h.TotalCount ?? 0) > 0
          ? ((h.okCount ?? h.ok_count ?? h.OkCount ?? 0) / (h.totalCount ?? h.total_count ?? h.TotalCount ?? 0)) * 100
          : 0,
      }));
      return;
    }
  } catch { /* 兜底 summary */ }

  try {
    const s = await getDailySummaryApi({ deviceId: deviceId.value, date }) as any;
    if (!s) return;
    const total = s.totalCount ?? 0;
    const ok    = s.okCount   ?? 0;
    const ng    = s.ngCount   ?? 0;
    rows.value = [
      { label: '白班 08:30-20:30', shift: 'D', total: s.dayShiftTotal??0, ok: s.dayShiftOk??0, ng: s.dayShiftNg??0, rate: s.dayShiftTotal>0?(s.dayShiftOk/s.dayShiftTotal)*100:0 },
      { label: '夜班 20:30-08:30', shift: 'N', total: s.nightShiftTotal??0, ok: s.nightShiftOk??0, ng: s.nightShiftNg??0, rate: s.nightShiftTotal>0?(s.nightShiftOk/s.nightShiftTotal)*100:0 },
    ].filter(r => r.total > 0);
    if (rows.value.length === 0 && total > 0) {
      rows.value = [{ label: date, shift: '-', total, ok, ng, rate: total>0?(ok/total)*100:0 }];
    }
  } catch { /* 无数据 */ }
};

// 月查询：单次请求 summary/range，取当月首末日
const fetchMonth = async (ym: string) => {
  const [year, month] = ym.split('-').map(Number) as [number, number];
  const mm        = String(month).padStart(2, '0');
  const lastDay   = new Date(year, month, 0).getDate();
  const startDate = `${year}-${mm}-01`;
  const endDate   = `${year}-${mm}-${String(lastDay).padStart(2, '0')}`;

  const list = await getSummaryRangeApi({ deviceId: deviceId.value, startDate, endDate }) as unknown as any[];
  rows.value = list
    .filter((s: any) => (s.totalCount ?? 0) > 0)
    .map((s: any) => {
      const total = s.totalCount ?? 0;
      const ok    = s.okCount    ?? 0;
      const ng    = s.ngCount    ?? 0;
      const d     = s.date?.slice(8, 10) ?? '';
      return { label: `${mm}-${d}`, shift: '', total, ok, ng, rate: total > 0 ? (ok / total) * 100 : 0 };
    });
};

// 年查询：单次请求 summary/range，取全年首末日，再按月分组聚合
const fetchYear = async (year: number) => {
  const startDate = `${year}-01-01`;
  const endDate   = `${year}-12-31`;

  const list = await getSummaryRangeApi({ deviceId: deviceId.value, startDate, endDate }) as unknown as any[];

  const byMonth: Record<number, { total: number; ok: number; ng: number }> = {};
  for (let m = 1; m <= 12; m++) byMonth[m] = { total: 0, ok: 0, ng: 0 };

  for (const s of list) {
    const m = parseInt((s.date as string).slice(5, 7), 10);
    byMonth[m]!.total += s.totalCount ?? 0;
    byMonth[m]!.ok    += s.okCount    ?? 0;
    byMonth[m]!.ng    += s.ngCount    ?? 0;
  }

  rows.value = Object.entries(byMonth).map(([m, v]) => ({
    label: `${m}月`, shift: '',
    total: v.total, ok: v.ok, ng: v.ng,
    rate: v.total > 0 ? (v.ok / v.total) * 100 : 0,
  }));
};

onMounted(() => fetchData());
</script>

<style scoped>
* { box-sizing: border-box; }
.detail-page { font-family: 'Noto Sans SC', sans-serif; color: #e0e4ef; }

.page-header { display: flex; align-items: center; gap: 16px; margin-bottom: 20px; }
.back-btn { display: inline-flex; align-items: center; gap: 6px; padding: 7px 12px; border-radius: 3px; border: 1px solid rgba(255,255,255,0.1); background: rgba(255,255,255,0.04); color: rgba(255,255,255,0.5); font-size: 13px; cursor: pointer; transition: all 0.15s; flex-shrink: 0; }
.back-btn:hover { color: #fff; border-color: rgba(255,255,255,0.2); }
.back-btn svg { width: 14px; height: 14px; }
.page-title { font-size: 20px; font-weight: 600; color: #fff; margin: 0 0 3px; }
.page-sub { font-size: 12px; color: rgba(255,255,255,0.3); margin: 0; }

.query-bar { display: flex; align-items: center; gap: 16px; margin-bottom: 16px; padding: 12px 16px; background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; }
.mode-tabs { display: flex; gap: 4px; }
.mode-tab { padding: 6px 14px; border-radius: 3px; border: 1px solid rgba(255,255,255,0.08); background: rgba(255,255,255,0.03); color: rgba(255,255,255,0.4); font-size: 13px; cursor: pointer; transition: all 0.15s; font-family: 'Noto Sans SC', sans-serif; }
.mode-tab:hover { border-color: rgba(0,229,255,0.25); color: rgba(255,255,255,0.7); }
.mode-tab.active { background: rgba(0,229,255,0.1); border-color: rgba(0,229,255,0.35); color: #00e5ff; }
.date-inputs { display: flex; gap: 8px; }
.filter-input { background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.1); border-radius: 3px; padding: 7px 10px; color: rgba(255,255,255,0.8); font-size: 13px; outline: none; transition: border-color 0.2s; font-family: 'Noto Sans SC', sans-serif; }
.filter-input:focus { border-color: rgba(0,229,255,0.4); }
select.filter-input { appearance: none; cursor: pointer; padding-right: 28px; background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath d='M3 4.5l3 3 3-3' stroke='%2300e5ff' stroke-width='1.2' fill='none' stroke-linecap='round'/%3E%3C/svg%3E"); background-repeat: no-repeat; background-position: right 10px center; }
select.filter-input option { background: #0f1525; color: #e0e4ef; }

.stat-cards { display: grid; grid-template-columns: repeat(5, 1fr); gap: 12px; margin-bottom: 16px; }
.stat-card { background: rgba(255,255,255,0.03); border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; padding: 14px 16px; display: flex; flex-direction: column; gap: 6px; }
.stat-card.ok   { border-color: rgba(0,229,160,0.15); }
.stat-card.ng   { border-color: rgba(255,77,79,0.15); }
.stat-card.rate { border-color: rgba(0,229,255,0.15); }
.stat-card.avg  { border-color: rgba(120,80,255,0.15); }
.stat-label { font-size: 11px; color: rgba(255,255,255,0.35); }
.stat-value { font-size: 22px; font-weight: 600; color: #fff; font-family: 'Courier New', monospace; }
.stat-card.ok   .stat-value { color: #00e5a0; }
.stat-card.ng   .stat-value { color: #ff8888; }
.stat-card.rate .stat-value { color: #00e5ff; }
.stat-card.avg  .stat-value { color: #b090ff; }

.chart-card { background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; padding: 16px 18px; margin-bottom: 16px; }
.chart-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px; }
.chart-title { font-size: 13px; font-weight: 500; color: rgba(255,255,255,0.7); }
.chart-legend { display: flex; gap: 14px; }
.legend-item { display: flex; align-items: center; gap: 5px; font-size: 11px; color: rgba(255,255,255,0.4); }
.legend-dot { width: 8px; height: 8px; border-radius: 2px; }
.legend-dot.ok { background: rgba(0,229,160,0.7); }
.legend-dot.ng { background: rgba(255,77,79,0.6); }
.chart-area { overflow-x: auto; padding-bottom: 8px; }
.chart-bars { display: flex; align-items: flex-end; gap: 6px; min-height: 170px; padding-bottom: 44px; border-bottom: 1px solid rgba(255,255,255,0.06); }
.chart-bars.dense-labels { min-width: 1680px; }
.chart-col { display: flex; flex-direction: column; align-items: center; gap: 4px; flex: 1; min-width: 28px; position: relative; }
.bar-total { font-size: 10px; color: rgba(255,255,255,0.4); font-family: 'Courier New', monospace; }
.bar-stack { display: flex; flex-direction: column; align-items: center; width: 100%; }
.bar { width: 100%; max-width: 36px; transition: height 0.4s ease; }
.bar.ok { background: rgba(0,229,160,0.6); border-radius: 3px 3px 0 0; }
.bar.ng { background: rgba(255,77,79,0.5); }
.bar-label { font-size: 10px; color: rgba(255,255,255,0.38); text-align: center; white-space: nowrap; margin-top: 8px; width: 44px; position: absolute; bottom: -36px; left: 50%; transform: translateX(-50%) rotate(-35deg); transform-origin: top center; }
.bar-label.placeholder { visibility: hidden; }
.chart-loading { display: flex; justify-content: center; padding: 40px 0; }
.chart-empty { text-align: center; padding: 40px 0; font-size: 13px; color: rgba(255,255,255,0.2); }
.loading-ring { width: 28px; height: 28px; border: 2px solid rgba(0,229,255,0.15); border-top-color: #00e5ff; border-radius: 50%; animation: spin 0.8s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }

@media (max-width: 768px) {
  .chart-bars.dense-labels { min-width: 1320px; }
  .bar-label { width: 38px; font-size: 9px; }
}

.table-wrap { background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; overflow: hidden; }
.data-table { width: 100%; border-collapse: collapse; }
.data-table thead tr { background: rgba(255,255,255,0.03); border-bottom: 1px solid rgba(255,255,255,0.06); }
.data-table th { padding: 10px 14px; text-align: left; font-size: 11px; font-weight: 500; color: rgba(255,255,255,0.3); letter-spacing: 1px; text-transform: uppercase; }
.table-row { border-bottom: 1px solid rgba(255,255,255,0.04); transition: background 0.15s; }
.table-row:last-child { border-bottom: none; }
.table-row:hover { background: rgba(0,229,255,0.03); }
.data-table td { padding: 11px 14px; font-size: 13px; vertical-align: middle; }
.mono { font-family: 'Courier New', monospace; font-size: 12px; }
.ok-num { color: #00e5a0; }
.ng-num { color: #ff8888; }
.rate-good { color: #00e5a0; }
.rate-warn { color: #ffb300; }
.rate-bad  { color: #ff8888; }
.shift-tag { font-size: 10px; background: rgba(0,229,255,0.08); color: rgba(0,229,255,0.7); padding: 2px 8px; border-radius: 3px; border: 1px solid rgba(0,229,255,0.15); font-weight: 500; }

.skeleton-rows { padding: 8px 0; }
.skeleton-row { display: flex; gap: 16px; padding: 13px 16px; border-bottom: 1px solid rgba(255,255,255,0.04); }
.skel { background: rgba(255,255,255,0.06); border-radius: 3px; height: 14px; animation: shimmer 1.5s infinite; }
.skel-sm { width: 70px; } .skel-md { width: 120px; }
@keyframes shimmer { 0%,100% { opacity:0.5; } 50% { opacity:1; } }

.empty-cell { text-align: center; padding: 48px 0; }
.empty-state p { font-size: 13px; color: rgba(255,255,255,0.25); margin: 0; }
</style>